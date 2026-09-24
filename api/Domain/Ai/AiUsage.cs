namespace Finance.Api.Domain.Ai;

/// <summary>
/// One call to an AI provider, and what it cost. The per-user budget (ADR-008) is the sum
/// of <see cref="CostBrl"/> over the user's month. A failed call is recorded too, with
/// <see cref="Succeeded"/> false, because its input tokens were spent (009, decision 3).
/// </summary>
/// <remarks>Append-only: nothing updates or deletes a row but the user's own deletion.</remarks>
public sealed class AiUsage : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The calendar month the call was made in, <c>YYYY-MM</c>.</summary>
    public string Month { get; set; } = "";

    public AiPurpose Purpose { get; set; }

    /// <summary>The configured provider's name, e.g. <c>anthropic</c>.</summary>
    public string Provider { get; set; } = "";

    /// <summary>The model identifier sent, as configured (009, decision 2).</summary>
    public string Model { get; set; } = "";

    public int InputTokens { get; set; }

    public int OutputTokens { get; set; }

    /// <summary>Money: <c>decimal</c>, four places, never a float (principle 4).</summary>
    public decimal CostBrl { get; set; }

    public bool Succeeded { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
