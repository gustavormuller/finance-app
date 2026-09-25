using System.Net;
using System.Net.Http.Json;
using Npgsql;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// The callback logic, running for real. Only the authorization code exchange is
/// replaced (see <see cref="TestGoogleHandler"/>); the branch that decides between
/// creating a user, linking to an existing one and refusing outright is production
/// code here.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class GoogleCallbackTests(PostgresFixture postgres)
{
    private const string CallbackPath = "/api/auth/google/callback";

    private sealed record MeResponse(Guid Id, string Email, string? DisplayName, bool AiEnabled);

    private static string UniqueEmail(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}@example.com";

    private static string UniqueSubject() => Guid.NewGuid().ToString("N");

    /// <summary>Spec test 2.</summary>
    [Fact]
    public async Task A_new_subject_with_a_verified_email_creates_a_user_and_a_session()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var email = UniqueEmail("new-user");
        var subject = UniqueSubject();

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        factory.GoogleAccount.Subject = subject;
        factory.GoogleAccount.Email = email;
        factory.GoogleAccount.DisplayName = "Ada Lovelace";

        using var client = factory.CreateApiClient();

        using var callback = await client.GetAsync(CallbackPath, cancellationToken);

        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        Assert.Equal("/", callback.Headers.Location?.ToString());

        Assert.True(
            callback.Headers.TryGetValues("Set-Cookie", out var setCookie),
            "the callback did not set any cookie");
        Assert.Contains(
            setCookie,
            header => header.StartsWith(IdentityApiFactory.SessionCookieName + "=", StringComparison.Ordinal));

        Assert.Equal(1, await CountUsersAsync(email, cancellationToken));
        Assert.Equal(1, await CountLoginsForSubjectAsync(subject, cancellationToken));

        using var me = await client.GetAsync("/api/auth/me", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        var body = await me.Content.ReadFromJsonAsync<MeResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(email, body.Email);
        Assert.Equal("Ada Lovelace", body.DisplayName);

        // ADR-010: AI ships disabled.
        Assert.False(body.AiEnabled);
    }

    /// <summary>Spec test 3.</summary>
    [Fact]
    public async Task The_same_subject_signing_in_twice_creates_one_user()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var email = UniqueEmail("returning");
        var subject = UniqueSubject();

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        factory.GoogleAccount.Subject = subject;
        factory.GoogleAccount.Email = email;
        factory.GoogleAccount.DisplayName = "Grace Hopper";

        var first = await SignInAsync(factory, cancellationToken);
        var second = await SignInAsync(factory, cancellationToken);

        Assert.Equal(first, second);
        Assert.Equal(1, await CountUsersAsync(email, cancellationToken));
        Assert.Equal(1, await CountLoginsForSubjectAsync(subject, cancellationToken));
    }

    /// <summary>
    /// Spec test 4 — the duplicate-account case ARCHITECTURE.md warns will happen.
    /// </summary>
    [Fact]
    public async Task A_new_subject_on_an_existing_email_is_linked_to_that_user()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var email = UniqueEmail("linked");
        var firstSubject = UniqueSubject();
        var secondSubject = UniqueSubject();

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        factory.GoogleAccount.Email = email;
        factory.GoogleAccount.DisplayName = "Katherine Johnson";

        factory.GoogleAccount.Subject = firstSubject;
        var existingUser = await SignInAsync(factory, cancellationToken);

        factory.GoogleAccount.Subject = secondSubject;
        var afterLinking = await SignInAsync(factory, cancellationToken);

        // Same person, not a second account.
        Assert.Equal(existingUser, afterLinking);
        Assert.Equal(1, await CountUsersAsync(email, cancellationToken));
        Assert.Equal(2, await CountLoginsForEmailAsync(email, cancellationToken));
        Assert.Equal(1, await CountLoginsForSubjectAsync(firstSubject, cancellationToken));
        Assert.Equal(1, await CountLoginsForSubjectAsync(secondSubject, cancellationToken));
    }

    /// <summary>Spec test 5.</summary>
    [Fact]
    public async Task An_unverified_email_creates_nothing_and_is_sent_back_to_login()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var email = UniqueEmail("unverified");
        var subject = UniqueSubject();

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        factory.GoogleAccount.Subject = subject;
        factory.GoogleAccount.Email = email;
        factory.GoogleAccount.DisplayName = "Unverified";
        factory.GoogleAccount.EmailVerified = false;

        using var client = factory.CreateApiClient();

        using var callback = await client.GetAsync(CallbackPath, cancellationToken);

        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        Assert.Equal("/login?error=unverified", callback.Headers.Location?.ToString());

        Assert.Equal(0, await CountUsersAsync(email, cancellationToken));
        Assert.Equal(0, await CountLoginsForSubjectAsync(subject, cancellationToken));

        using var me = await client.GetAsync("/api/auth/me", cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    /// <summary>
    /// Refusing the consent screen. Not in the spec's test plan — the spec did not
    /// cover the path at all, and without a handler it answers 500.
    /// </summary>
    [Fact]
    public async Task A_refused_consent_is_sent_back_to_login_and_creates_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var email = UniqueEmail("refused");
        var subject = UniqueSubject();

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        factory.GoogleAccount.Subject = subject;
        factory.GoogleAccount.Email = email;

        using var client = factory.CreateApiClient();

        using var callback = await client.GetAsync(
            $"{CallbackPath}?error=access_denied",
            cancellationToken);

        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        Assert.Equal("/login?error=cancelled", callback.Headers.Location?.ToString());

        Assert.Equal(0, await CountUsersAsync(email, cancellationToken));
        Assert.Equal(0, await CountLoginsForSubjectAsync(subject, cancellationToken));

        using var me = await client.GetAsync("/api/auth/me", cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, me.StatusCode);
    }

    /// <summary>
    /// The other half of the same branch. Without it, a wrong constant in the
    /// non-refusal case would go unnoticed.
    /// </summary>
    [Fact]
    public async Task Any_other_google_failure_is_sent_back_to_login_as_a_failure()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var email = UniqueEmail("failed");

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        factory.GoogleAccount.Subject = UniqueSubject();
        factory.GoogleAccount.Email = email;

        using var client = factory.CreateApiClient();

        using var callback = await client.GetAsync(
            $"{CallbackPath}?error=temporarily_unavailable",
            cancellationToken);

        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        Assert.Equal("/login?error=auth_failed", callback.Headers.Location?.ToString());

        Assert.Equal(0, await CountUsersAsync(email, cancellationToken));
    }

    /// <summary>Runs one callback and returns the id of the user it signed in.</summary>
    private static async Task<Guid> SignInAsync(
        IdentityApiFactory factory,
        CancellationToken cancellationToken)
    {
        using var client = factory.CreateApiClient();

        using var callback = await client.GetAsync(CallbackPath, cancellationToken);
        Assert.Equal(HttpStatusCode.Found, callback.StatusCode);
        Assert.Equal("/", callback.Headers.Location?.ToString());

        using var me = await client.GetAsync("/api/auth/me", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        var body = await me.Content.ReadFromJsonAsync<MeResponse>(cancellationToken);
        Assert.NotNull(body);

        return body.Id;
    }

    private Task<long> CountUsersAsync(string email, CancellationToken cancellationToken) =>
        CountAsync(
            "SELECT count(*) FROM \"AspNetUsers\" WHERE \"Email\" = @value;",
            email,
            cancellationToken);

    private Task<long> CountLoginsForSubjectAsync(string subject, CancellationToken cancellationToken) =>
        CountAsync(
            "SELECT count(*) FROM \"AspNetUserLogins\" "
            + "WHERE \"LoginProvider\" = 'Google' AND \"ProviderKey\" = @value;",
            subject,
            cancellationToken);

    private Task<long> CountLoginsForEmailAsync(string email, CancellationToken cancellationToken) =>
        CountAsync(
            "SELECT count(*) FROM \"AspNetUserLogins\" l "
            + "JOIN \"AspNetUsers\" u ON u.\"Id\" = l.\"UserId\" "
            + "WHERE u.\"Email\" = @value;",
            email,
            cancellationToken);

    /// <summary>
    /// Read with raw SQL rather than through the DbContext: what is asserted is the
    /// state of the rows, independently of the code that wrote them.
    /// </summary>
    private async Task<long> CountAsync(string sql, string value, CancellationToken cancellationToken)
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("value", value);

        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }
}
