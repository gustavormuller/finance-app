using System.Net;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 010 spec test 4. In production the API sits behind Cloudflare, cloudflared and Caddy,
/// and only Caddy talks to it, over plain HTTP on the Docker network. The scheme the
/// browser used arrives as <c>X-Forwarded-Proto</c>, and the browser's address as the
/// first entry of <c>X-Forwarded-For</c>. Both are honoured only from a peer inside
/// <c>ForwardedHeaders:KnownNetworks</c>.
/// </summary>
/// <remarks>
/// The observable effects are the Google flow's: the <c>redirect_uri</c> is built from
/// <c>Request.Scheme</c> and must be <c>https</c> to match the Cloud Console, and the
/// correlation cookie must be <c>Secure</c>, or the browser drops it and the callback
/// fails. The per-IP sign-in rate limit must count browsers, not the proxy.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class ForwardedHeadersTests(PostgresFixture postgres)
{
    private const string PeerHeader = "X-Test-Peer";
    private const string DockerNetwork = "172.30.0.0/24";
    private const string Caddy = "172.30.0.5";
    private const string Stranger = "203.0.113.9";

    private IdentityApiFactory Factory() => new(
        postgres.ConnectionString,
        environment: "Production",
        services: services => services.AddTransient<IStartupFilter, PeerAddressFilter>(),
        settings: new Dictionary<string, string?> { ["ForwardedHeaders:KnownNetworks:0"] = DockerNetwork });

    [Fact]
    public async Task A_trusted_proxy_makes_the_google_redirect_https_and_its_cookie_secure()
    {
        await using var factory = Factory();
        using var client = PlainHttpClient(factory);

        using var response = await client.SendAsync(Challenge(Caddy, proto: "https"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains(
            "redirect_uri=" + Uri.EscapeDataString("https://finance.example/api/auth/google/callback"),
            response.Headers.Location!.AbsoluteUri,
            StringComparison.Ordinal);
        Assert.Contains("secure", CorrelationCookie(response), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task An_untrusted_peer_cannot_claim_https()
    {
        await using var factory = Factory();
        using var client = PlainHttpClient(factory);

        using var response = await client.SendAsync(Challenge(Stranger, proto: "https"), TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Found, response.StatusCode);
        Assert.Contains(
            "redirect_uri=" + Uri.EscapeDataString("http://finance.example/api/auth/google/callback"),
            response.Headers.Location!.AbsoluteUri,
            StringComparison.Ordinal);
    }

    /// <summary>
    /// Without X-Forwarded-For every browser would share Caddy's address, and ten
    /// sign-ins a minute would be the whole site's allowance (ADR-010's rate limit).
    /// </summary>
    [Fact]
    public async Task Behind_a_trusted_proxy_the_sign_in_limit_counts_each_browser()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = Factory();
        using var client = PlainHttpClient(factory);

        for (var attempt = 0; attempt < 10; attempt++)
        {
            using var allowed = await client.SendAsync(Challenge(Caddy, "https", forwardedFor: "198.51.100.1"), ct);
            Assert.Equal(HttpStatusCode.Found, allowed.StatusCode);
        }

        using var sameBrowser = await client.SendAsync(Challenge(Caddy, "https", forwardedFor: "198.51.100.1"), ct);
        using var otherBrowser = await client.SendAsync(Challenge(Caddy, "https", forwardedFor: "198.51.100.2"), ct);

        Assert.Equal(HttpStatusCode.TooManyRequests, sameBrowser.StatusCode);
        Assert.Equal(HttpStatusCode.Found, otherBrowser.StatusCode);
    }

    private static HttpClient PlainHttpClient(IdentityApiFactory factory)
    {
        var client = factory.CreateApiClient();
        client.BaseAddress = new Uri("http://finance.example");
        return client;
    }

    private static HttpRequestMessage Challenge(string peer, string proto, string? forwardedFor = null)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "/api/auth/google");
        request.Headers.Add(PeerHeader, peer);
        request.Headers.Add("X-Forwarded-Proto", proto);
        if (forwardedFor is not null)
        {
            request.Headers.Add("X-Forwarded-For", forwardedFor);
        }

        return request;
    }

    private static string CorrelationCookie(HttpResponseMessage response) =>
        response.Headers.GetValues("Set-Cookie")
            .Single(cookie => cookie.StartsWith(".AspNetCore.Correlation.", StringComparison.Ordinal));

    /// <summary>
    /// TestServer has no socket, so the peer address is set from a test-only header,
    /// ahead of the whole application pipeline.
    /// </summary>
    private sealed class PeerAddressFilter : IStartupFilter
    {
        public Action<IApplicationBuilder> Configure(Action<IApplicationBuilder> next) => app =>
        {
            app.Use((HttpContext context, RequestDelegate proceed) =>
            {
                if (IPAddress.TryParse(context.Request.Headers[PeerHeader], out var peer))
                {
                    context.Connection.RemoteIpAddress = peer;
                }

                return proceed(context);
            });
            next(app);
        };
    }
}
