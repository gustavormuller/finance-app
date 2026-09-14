using System.Security.Claims;
using System.Text.Json;
using Finance.Api.Application;
using Finance.Api.Infrastructure;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// The one piece of the Google flow no integration test can reach: it runs inside the
/// code exchange, which the test handler stands in for. Everything here is a pure
/// function over a payload, so it needs no host and no database.
/// </summary>
public sealed class GoogleUserInfoTests
{
    /// <summary>The shape Google actually returns, boolean <c>email_verified</c> included.</summary>
    private const string GooglePayload =
        """
        {
          "sub": "104283920398712345678",
          "name": "Ada Lovelace",
          "given_name": "Ada",
          "family_name": "Lovelace",
          "picture": "https://lh3.googleusercontent.com/a/example",
          "email": "ada@example.com",
          "email_verified": true
        }
        """;

    [Fact]
    public void A_verified_address_produces_one_true_claim()
    {
        var claim = Assert.Single(ClaimsFrom(GooglePayload));

        Assert.Equal(ExternalSignIn.EmailVerifiedClaimType, claim.Type);
        Assert.Equal("true", claim.Value);

        // Nothing else is produced: sub, email and name arrive through Google's own
        // claim actions, and duplicating them here would fight with those.
        Assert.True(ExternalSignIn.IsEmailVerified(PrincipalFrom(GooglePayload)));
    }

    [Fact]
    public void An_unverified_address_produces_an_explicit_false_claim()
    {
        const string payload = """{"email": "ada@example.com", "email_verified": false}""";

        var claim = Assert.Single(ClaimsFrom(payload));

        Assert.Equal("false", claim.Value);
        Assert.False(ExternalSignIn.IsEmailVerified(PrincipalFrom(payload)));
    }

    [Fact]
    public void A_value_that_is_not_a_boolean_produces_no_claim_at_all()
    {
        // OpenID Connect defines this as a boolean. A provider sending the string
        // "true" has not made the statement the claim is supposed to carry, so nothing
        // is asserted on its behalf.
        Assert.Empty(ClaimsFrom("""{"email_verified": "true"}"""));
    }

    [Theory]
    [InlineData("""{"email_verified": false}""")]
    [InlineData("""{"email_verified": "true"}""")]
    [InlineData("""{"email_verified": "false"}""")]
    [InlineData("""{"email_verified": 1}""")]
    [InlineData("""{"email_verified": null}""")]
    [InlineData("""{"email": "ada@example.com"}""")]
    [InlineData("{}")]
    public void Anything_but_a_boolean_true_reads_as_not_verified(string payload)
    {
        // Fail closed. Every one of these has to be treated as "Google did not vouch
        // for this address", because the consequence of getting it wrong is an account
        // created for an address the person may not own.
        Assert.False(ExternalSignIn.IsEmailVerified(PrincipalFrom(payload)));
    }

    private static IReadOnlyList<Claim> ClaimsFrom(string payload)
    {
        using var document = JsonDocument.Parse(payload);

        return GoogleUserInfo.ReadUnmappedClaims(document.RootElement);
    }

    private static ClaimsPrincipal PrincipalFrom(string payload) =>
        new(new ClaimsIdentity(ClaimsFrom(payload), "Google"));
}
