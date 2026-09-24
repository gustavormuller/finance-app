namespace Finance.Api.Application.Ai;

/// <summary>
/// The per-user monthly ceiling (ADR-008): a hard cut-off before every call.
/// </summary>
public sealed class BudgetGuard
{
    /// <summary>What the user has spent in <paramref name="month"/>, failed calls included.</summary>
    public Task<decimal> SpentAsync(Guid userId, string month, CancellationToken ct) => throw new NotImplementedException();

    /// <exception cref="AiBudgetExceededException">The user's month has reached the budget.</exception>
    public Task EnsureWithinBudgetAsync(Guid userId, string month, CancellationToken ct) => throw new NotImplementedException();

    /// <summary>Whether a user who has spent <paramref name="spentBrl"/> may make another call.</summary>
    /// <remarks>
    /// Refused at <c>spent &gt;= budget</c> (spec tests 1-2). The check runs before the call, so
    /// the call that crosses the line still completes: a month can end over the budget by at
    /// most one call's cost.
    /// </remarks>
    public static bool Allows(decimal spentBrl, decimal budgetBrl) => spentBrl < budgetBrl;
}
