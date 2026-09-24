namespace Finance.Api.Application.Ai;

/// <summary>
/// The per-user monthly ceiling (ADR-008): a hard cut-off before every call.
/// </summary>
public sealed class BudgetGuard
{
    /// <summary>Whether a user who has spent <paramref name="spentBrl"/> may make another call.</summary>
    public static bool Allows(decimal spentBrl, decimal budgetBrl) => throw new NotImplementedException();
}
