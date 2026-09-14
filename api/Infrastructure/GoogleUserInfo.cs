using System.Security.Claims;
using System.Text.Json;
using Finance.Api.Application;

namespace Finance.Api.Infrastructure;

/// <summary>
/// Google's userinfo payload, read into claims.
/// </summary>
public static class GoogleUserInfo
{
    /// <summary>
    /// The claims Google's own claim actions do not produce. Today that is
    /// <c>email_verified</c> and nothing else — <c>sub</c>, <c>email</c> and
    /// <c>name</c> already arrive mapped.
    /// </summary>
    /// <remarks>
    /// Fails closed. OpenID Connect defines <c>email_verified</c> as a boolean, so a
    /// string, a number or a missing key is not Google vouching for the address, and
    /// produces no claim at all. A missing claim reads as not verified downstream,
    /// which is the safe direction: the alternative is creating an account for an
    /// address the person may not own.
    /// <para>
    /// A separate function rather than an inline lambda because it is the one piece of
    /// the flow no integration test can reach — it runs inside the half the test
    /// handler stands in for — so it has to be reachable from a unit test instead.
    /// </para>
    /// </remarks>
    public static IReadOnlyList<Claim> ReadUnmappedClaims(JsonElement userInfo)
    {
        var claims = new List<Claim>();

        if (userInfo.TryGetProperty(ExternalSignIn.EmailVerifiedClaimType, out var emailVerified)
            && emailVerified.ValueKind is JsonValueKind.True or JsonValueKind.False)
        {
            claims.Add(new Claim(
                ExternalSignIn.EmailVerifiedClaimType,
                emailVerified.GetBoolean() ? "true" : "false"));
        }

        return claims;
    }
}
