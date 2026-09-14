using System.Security.Claims;
using Finance.Api.Domain.Identity;
using Microsoft.AspNetCore.Identity;

namespace Finance.Api.Application;

public enum ExternalSignInOutcome
{
    SignedIn,

    /// <summary>
    /// The provider did not vouch for the address. No user is created and no session
    /// is issued: an unverified address is an address someone else may own.
    /// </summary>
    EmailNotVerified,
}

/// <summary>
/// Turns an external provider's claims into one of our users and one of our cookies.
/// </summary>
/// <remarks>
/// Google is used to establish identity once; the session is ours from then on, so a
/// consent screen in Testing mode expiring its refresh token after seven days cannot
/// sign anybody out (see ARCHITECTURE.md, Authentication).
/// <para>
/// Also drives the development-only login endpoint, which is why the provider is a
/// parameter: one find-or-create path, exercised by both.
/// </para>
/// </remarks>
public sealed class ExternalSignIn(
    UserManager<AppUser> users,
    SignInManager<AppUser> signInManager,
    ILogger<ExternalSignIn> logger)
{
    /// <summary>
    /// Declared here rather than next to the provider that produces it, because both
    /// the Google mapping and the development login have to agree with the check
    /// below on the spelling.
    /// </summary>
    public const string EmailVerifiedClaimType = "email_verified";

    public async Task<ExternalSignInOutcome> SignInAsync(string provider, ClaimsPrincipal externalUser)
    {
        var email = externalUser.FindFirstValue(ClaimTypes.Email)
            ?? throw new InvalidOperationException($"{provider} returned no email claim.");

        if (!IsEmailVerified(externalUser))
        {
            // Deliberately before any write: an unverified address must leave no trace.
            logger.LogWarning(
                "Refused a {Provider} sign-in because the email address is not verified.",
                provider);

            return ExternalSignInOutcome.EmailNotVerified;
        }

        var subject = externalUser.FindFirstValue(ClaimTypes.NameIdentifier)
            ?? throw new InvalidOperationException($"{provider} returned no subject claim.");

        var displayName = externalUser.FindFirstValue(ClaimTypes.Name);
        var login = new UserLoginInfo(provider, subject, provider);

        var user = await users.FindByLoginAsync(provider, subject);

        if (user is null)
        {
            // The duplicate-account case ARCHITECTURE.md warns will happen: same person,
            // same address, a subject we have not seen. Link it rather than create a
            // second account holding half their history.
            user = await users.FindByEmailAsync(email);

            if (user is not null)
            {
                Verify(await users.AddLoginAsync(user, login), $"link {provider} login");

                logger.LogInformation(
                    "Linked a new {Provider} login to the existing account {UserId}.",
                    provider,
                    user.Id);
            }
        }

        if (user is null)
        {
            user = new AppUser
            {
                Email = email,
                UserName = email,
                DisplayName = displayName,
                AiEnabled = false,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            Verify(await users.CreateAsync(user), "create user");
            Verify(await users.AddLoginAsync(user, login), $"add {provider} login");

            logger.LogInformation("Created account {UserId} from a {Provider} sign-in.", user.Id, provider);
        }

        await signInManager.SignInAsync(user, isPersistent: true);

        return ExternalSignInOutcome.SignedIn;
    }

    /// <summary>
    /// A missing claim counts as not verified, and so does one that will not parse.
    /// </summary>
    /// <remarks>
    /// Internal rather than private so the unit tests can drive the fail-closed
    /// property through the real check instead of a copy of it.
    /// </remarks>
    internal static bool IsEmailVerified(ClaimsPrincipal externalUser) =>
        bool.TryParse(externalUser.FindFirstValue(EmailVerifiedClaimType), out var verified)
        && verified;

    private static void Verify(IdentityResult result, string operation)
    {
        if (result.Succeeded)
        {
            return;
        }

        // Nothing here is recoverable at runtime: a failure means the user store
        // rejected a write we built, which is a bug, not a user error.
        throw new InvalidOperationException(
            $"Could not {operation}: "
            + string.Join("; ", result.Errors.Select(error => $"{error.Code} {error.Description}")));
    }
}
