using System.Net;
using System.Net.Http.Json;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// The session itself: who the API thinks is calling, how a session ends, and the two
/// guards around the endpoints that create one.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class AuthSessionTests(PostgresFixture postgres)
{
    private sealed record MeResponse(Guid Id, string Email, string? DisplayName, bool AiEnabled);

    private static string UniqueEmail(string prefix) =>
        $"{prefix}-{Guid.NewGuid():N}@example.com";

    /// <summary>Spec test 1.</summary>
    [Fact]
    public async Task Me_answers_401_without_a_session_and_does_not_redirect()
    {
        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync("/api/auth/me", TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);

        // Identity's cookie default is a 302 to /Account/Login. For a JSON API that is
        // a bug, and the SPA would follow it into a page that does not exist.
        Assert.Null(response.Headers.Location);
    }

    /// <summary>Spec test 6.</summary>
    [Fact]
    public async Task Logout_with_a_matching_origin_clears_the_session()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var email = UniqueEmail("logout-ok");

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateSignedInClientAsync(email, "Logout Ok", cancellationToken);

        using var before = await client.GetAsync("/api/auth/me", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, before.StatusCode);

        using var logout = await client.SendAsync(AuthRequests.Logout(), cancellationToken);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);

        using var after = await client.GetAsync("/api/auth/me", cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, after.StatusCode);
    }

    /// <summary>Spec test 7.</summary>
    [Fact]
    public async Task Logout_from_a_foreign_origin_is_refused_and_leaves_the_session_alone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var email = UniqueEmail("logout-origin");

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateSignedInClientAsync(email, "Logout Origin", cancellationToken);

        using var logout = await client.SendAsync(
            AuthRequests.Logout(origin: "https://evil.example"),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, logout.StatusCode);

        // The point of the test: refusing the request must not have been a slow way of
        // performing it.
        using var me = await client.GetAsync("/api/auth/me", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);

        var body = await me.Content.ReadFromJsonAsync<MeResponse>(cancellationToken);
        Assert.NotNull(body);
        Assert.Equal(email, body.Email);
    }

    /// <summary>
    /// Spec test 7, second half: a mutating request with no Origin header at all is
    /// refused too, otherwise the check is trivially bypassed by omitting it.
    /// </summary>
    [Fact]
    public async Task Logout_without_an_origin_header_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        using var client = await factory.CreateSignedInClientAsync(
            UniqueEmail("logout-no-origin"),
            "No Origin",
            cancellationToken);

        using var logout = await client.SendAsync(AuthRequests.Logout(origin: null), cancellationToken);

        Assert.Equal(HttpStatusCode.Forbidden, logout.StatusCode);
    }

    /// <summary>Spec test 9.</summary>
    [Fact]
    public async Task Eleventh_google_challenge_from_one_ip_within_a_minute_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        var statuses = new List<HttpStatusCode>();

        for (var attempt = 0; attempt < 11; attempt++)
        {
            using var response = await client.GetAsync("/api/auth/google", cancellationToken);
            statuses.Add(response.StatusCode);
        }

        Assert.All(
            statuses.Take(10),
            status => Assert.Equal(HttpStatusCode.Found, status));

        Assert.Equal(HttpStatusCode.TooManyRequests, statuses[10]);
    }

    /// <summary>
    /// Spec test 10. Asserts both halves: absent outside Development, and present
    /// inside it. Without the second assertion the test would pass against an
    /// application that has no such endpoint at all.
    /// </summary>
    [Fact]
    public async Task Dev_login_exists_in_development_and_nowhere_else()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var production = new IdentityApiFactory(
            postgres.ConnectionString,
            environment: "Production");
        using var productionClient = production.CreateApiClient();

        using var inProduction = await productionClient.SendAsync(
            AuthRequests.DevLogin(UniqueEmail("dev-login-prod"), "Nope"),
            cancellationToken);

        // 404, not 403: the route must not exist, rather than exist and refuse.
        Assert.Equal(HttpStatusCode.NotFound, inProduction.StatusCode);

        await using var development = new IdentityApiFactory(postgres.ConnectionString);
        using var developmentClient = development.CreateApiClient();

        using var inDevelopment = await developmentClient.SendAsync(
            AuthRequests.DevLogin(UniqueEmail("dev-login-dev"), "Yes"),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, inDevelopment.StatusCode);
    }
}
