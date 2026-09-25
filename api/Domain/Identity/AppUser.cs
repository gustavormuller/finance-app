using Microsoft.AspNetCore.Identity;

namespace Finance.Api.Domain.Identity;

/// <summary>
/// The application user. <see cref="IdentityUser{TKey}"/> keyed by
/// <see cref="Guid"/> rather than Identity's default <c>string</c>, so every foreign
/// key pointing here is a real uuid.
/// </summary>
/// <remarks>
/// <c>Microsoft.AspNetCore.Identity</c> lives in Microsoft.Extensions.Identity.Stores
/// and carries no EF Core dependency, so this stays inside the Domain rule.
/// </remarks>
public sealed class AppUser : IdentityUser<Guid>
{
    /// <summary>
    /// Whether this user may spend money on the AI module. ADR-008 and ADR-010: the
    /// one resource with a marginal cost, off by default, switched on per person by
    /// hand.
    /// </summary>
    public bool AiEnabled { get; set; }

    /// <summary>From Google's <c>name</c> claim. Nothing depends on it being set.</summary>
    public string? DisplayName { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
