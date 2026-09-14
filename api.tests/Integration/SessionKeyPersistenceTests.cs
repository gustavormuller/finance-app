using System.Net;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// Spec test 8. Two independent hosts sharing one Data Protection key ring: a cookie
/// issued by one has to be readable by the other.
/// </summary>
/// <remarks>
/// This is the restart case. Without persisted keys each process generates its own
/// ring, every session dies on deploy, and the symptom — "it logs everyone out when
/// I redeploy" — shows up in production and nowhere else. Two factories reproduce it
/// in-process.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class SessionKeyPersistenceTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_cookie_issued_by_one_host_is_accepted_by_another_sharing_the_key_ring()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var sharedKeys = Path.Combine(Path.GetTempPath(), "finance-app-tests", Guid.NewGuid().ToString("N"));
        var email = $"persisted-{Guid.NewGuid():N}@example.com";

        string sessionCookie;

        await using (var issuer = new IdentityApiFactory(postgres.ConnectionString, sharedKeys))
        {
            using var issuerClient = issuer.CreateApiClient();

            using var devLogin = await issuerClient.SendAsync(
                AuthRequests.DevLogin(email, "Persisted Keys"),
                cancellationToken);

            Assert.Equal(HttpStatusCode.NoContent, devLogin.StatusCode);

            sessionCookie = IdentityApiFactory.ReadSessionCookie(devLogin);
        }

        // A second host, built from scratch, that has never issued this cookie.
        await using var reader = new IdentityApiFactory(postgres.ConnectionString, sharedKeys);
        using var readerClient = reader.CreateApiClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("Cookie", sessionCookie);

        using var response = await readerClient.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    /// <summary>
    /// The control for the test above: with separate key rings the same cookie is
    /// rejected. Without this, a host that ignored the cookie's signature entirely
    /// would pass the first test.
    /// </summary>
    [Fact]
    public async Task A_cookie_issued_by_one_host_is_rejected_by_another_with_its_own_key_ring()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var email = $"separate-{Guid.NewGuid():N}@example.com";

        string sessionCookie;

        await using (var issuer = new IdentityApiFactory(postgres.ConnectionString))
        {
            using var issuerClient = issuer.CreateApiClient();

            using var devLogin = await issuerClient.SendAsync(
                AuthRequests.DevLogin(email, "Separate Keys"),
                cancellationToken);

            Assert.Equal(HttpStatusCode.NoContent, devLogin.StatusCode);

            sessionCookie = IdentityApiFactory.ReadSessionCookie(devLogin);
        }

        await using var stranger = new IdentityApiFactory(postgres.ConnectionString);
        using var strangerClient = stranger.CreateApiClient();

        using var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        request.Headers.Add("Cookie", sessionCookie);

        using var response = await strangerClient.SendAsync(request, cancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
