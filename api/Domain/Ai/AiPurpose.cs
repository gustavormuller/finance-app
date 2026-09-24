namespace Finance.Api.Domain.Ai;

/// <summary>What an <see cref="AiUsage"/> row paid for. Stored as <c>int</c>, values written down.</summary>
public enum AiPurpose
{
    /// <summary>Rung 3 of ADR-012's cascade, from the import preview. The cheap model.</summary>
    Categorisation = 0,

    /// <summary>The monthly analysis on the dashboard. The better model.</summary>
    Analysis = 1,
}
