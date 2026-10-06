using System.Security.Claims;
using Finance.Api.Application;
using Finance.Api.Domain.Identity;
using Finance.Api.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Google;
using Microsoft.AspNetCore.Identity;

namespace Finance.Api.Endpoints;

public static class AuthEndpoints
{
    /// <summary>
    /// The provider recorded for sessions created by the development-only endpoint, so
    /// they are distinguishable from real Google logins in <c>AspNetUserLogins</c>.
    /// </summary>
    private const string DevelopmentProvider = "Development";

    private sealed record MeResponse(Guid Id, string Email, string? DisplayName, bool AiEnabled);

    private sealed record MePatch(bool? AiEnabled);

    private sealed record DevLoginRequest(string Email, string? DisplayName);

    public static IEndpointRouteBuilder MapAuthEndpoints(
        this IEndpointRouteBuilder routes,
        IHostEnvironment environment)
    {
        // The Google handler builds the redirect_uri it sends from the incoming Host
        // header, which is why the Vite dev proxy must not rewrite it.
        routes.MapGet("/api/auth/google", () => Results.Challenge(
                new AuthenticationProperties { RedirectUri = AuthenticationSetup.SignedInPath },
                [GoogleDefaults.AuthenticationScheme]))
            .RequireRateLimiting(AuthenticationSetup.GoogleSignInPolicy);

        // /api/auth/google/callback needs no endpoint: it is the Google handler's
        // CallbackPath, so the authentication middleware handles the request before
        // routing ever gets to it.

        routes.MapGet("/api/auth/me", async (ClaimsPrincipal principal, UserManager<AppUser> users) =>
            {
                var user = await users.GetUserAsync(principal);

                // A valid cookie for a user row that no longer exists is not a session.
                return user is null
                    ? Results.Unauthorized()
                    : Results.Ok(new MeResponse(user.Id, user.Email!, user.DisplayName, user.AiEnabled));
            })
            .RequireAuthorization();

        // The user's own ai_enabled (ADR-010), the only way to turn AI on from the app.
        // Answers with the whole of GET /api/auth/me, aiEnabled included.
        routes.MapPatch("/api/auth/me", async (MePatch patch, ClaimsPrincipal principal, UserManager<AppUser> users) =>
            {
                if (patch.AiEnabled is not { } aiEnabled)
                {
                    return Problems.Validation("aiEnabled", "Informe se a IA deve ficar ligada ou desligada.");
                }

                var user = await users.GetUserAsync(principal);
                if (user is null)
                {
                    return Results.Unauthorized();
                }

                user.AiEnabled = aiEnabled;
                var saved = await users.UpdateAsync(user);
                if (!saved.Succeeded)
                {
                    throw new InvalidOperationException($"Could not save ai_enabled: {string.Join("; ", saved.Errors.Select(error => error.Code))}");
                }

                return Results.Ok(new MeResponse(user.Id, user.Email!, user.DisplayName, user.AiEnabled));
            })
            .RequireAuthorization();

        routes.MapPost("/api/auth/logout", async (SignInManager<AppUser> signInManager) =>
            {
                await signInManager.SignOutAsync();

                return Results.NoContent();
            })
            .RequireAuthorization();

        if (environment.IsDevelopment())
        {
            MapDevLogin(routes);
        }

        return routes;
    }

    /// <summary>
    /// Playwright cannot complete a real Google flow, so E2E establishes its session
    /// here. Mapped only in Development: in any other environment the route does not
    /// exist at all, which is a 404 rather than a 403 that could be argued with.
    /// </summary>
    private static void MapDevLogin(IEndpointRouteBuilder routes) =>
        routes.MapPost("/api/auth/dev-login", async (
            DevLoginRequest request,
            ExternalSignIn externalSignIn) =>
        {
            if (string.IsNullOrWhiteSpace(request.Email))
            {
                return Results.BadRequest();
            }

            var claims = new List<Claim>
            {
                // Stable, so signing in twice with the same address finds the same user
                // through the same branch a returning Google user takes.
                new(ClaimTypes.NameIdentifier, $"dev-{request.Email}"),
                new(ClaimTypes.Email, request.Email),
                new(ExternalSignIn.EmailVerifiedClaimType, "true"),
            };

            if (!string.IsNullOrWhiteSpace(request.DisplayName))
            {
                claims.Add(new Claim(ClaimTypes.Name, request.DisplayName));
            }

            var principal = new ClaimsPrincipal(new ClaimsIdentity(claims, DevelopmentProvider));

            await externalSignIn.SignInAsync(DevelopmentProvider, principal);

            return Results.NoContent();
        });
}
