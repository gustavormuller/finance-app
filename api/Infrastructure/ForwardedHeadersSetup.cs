using Microsoft.AspNetCore.HttpOverrides;

namespace Finance.Api.Infrastructure;

/// <summary>
/// 010: in production only Caddy talks to the API, over plain HTTP on the Docker
/// network, so the browser's scheme and address arrive as <c>X-Forwarded-Proto</c> and
/// <c>X-Forwarded-For</c>. Without the scheme the Google <c>redirect_uri</c> is built as
/// <c>http://</c> and never matches the Cloud Console entry.
/// </summary>
public static class ForwardedHeadersSetup
{
    /// <summary>
    /// CIDR ranges whose peers may set the forwarded headers, besides loopback. Set in
    /// <c>deploy/docker-compose.yml</c> to the Docker network it pins. Empty everywhere
    /// else, so no request can claim a scheme or an address it did not arrive with.
    /// </summary>
    public const string KnownNetworksKey = "ForwardedHeaders:KnownNetworks";

    public static IServiceCollection AddFinanceForwardedHeaders(this IServiceCollection services)
    {
        services.AddOptions<ForwardedHeadersOptions>()
            .Configure<IConfiguration>((options, configuration) =>
            {
                options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

                // Cloudflare, then cloudflared, then Caddy: two hops on the Docker network before
                // the browser's own address. Unwinding stops at the first untrusted peer
                // anyway, so no fixed limit is needed.
                options.ForwardLimit = null;

                foreach (var network in configuration.GetSection(KnownNetworksKey).Get<string[]>() ?? [])
                {
                    options.KnownIPNetworks.Add(System.Net.IPNetwork.Parse(network));
                }
            });

        return services;
    }
}
