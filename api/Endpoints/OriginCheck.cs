using Microsoft.Net.Http.Headers;

namespace Finance.Api.Endpoints;

/// <summary>
/// CSRF protection for a same-origin SPA: every mutating request to <c>/api</c> has to
/// carry an <c>Origin</c> header matching the configured application origin.
/// </summary>
/// <remarks>
/// Antiforgery tokens would add a token endpoint, a header contract and a failure mode
/// for no gain here. <c>SameSite=Lax</c> already stops a cross-site form POST from
/// carrying the session cookie; this closes what is left, deterministically, and is
/// small enough to test directly.
/// <para>
/// A missing header is refused rather than allowed, otherwise the check is bypassed by
/// omitting it.
/// </para>
/// </remarks>
public static class OriginCheck
{
    public static IApplicationBuilder UseApiOriginCheck(this IApplicationBuilder app, string appOrigin) =>
        app.Use(async (context, next) =>
        {
            if (!context.Request.Path.StartsWithSegments("/api") || IsSafe(context.Request.Method))
            {
                await next(context);
                return;
            }

            var origin = context.Request.Headers[HeaderNames.Origin].ToString();

            if (!string.Equals(origin, appOrigin, StringComparison.Ordinal))
            {
                // Before the endpoint, and before authorization: a refused request must
                // not have been a slow way of performing it.
                context.Response.StatusCode = StatusCodes.Status403Forbidden;
                return;
            }

            await next(context);
        });

    private static bool IsSafe(string method) =>
        HttpMethods.IsGet(method) || HttpMethods.IsHead(method) || HttpMethods.IsOptions(method);
}
