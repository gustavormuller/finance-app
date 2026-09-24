using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure;

namespace Finance.Api.Application.Dashboard;

/// <summary>One account on the dashboard, with its current balance.</summary>
public sealed record AccountBalance(
    Guid AccountId,
    string Name,
    AccountType Type,
    string Currency,
    decimal Balance,
    bool ExcludedFromTotal);

/// <summary>Income, expense and their sum over one calendar month.</summary>
public sealed record MonthSummary(decimal Income, decimal Expense, decimal Net);

public sealed record DashboardSummary(
    IReadOnlyList<AccountBalance> Balances,
    decimal Total,
    MonthSummary Month);

/// <summary>One calendar month of the series, <c>Month</c> as <c>YYYY-MM</c>.</summary>
public sealed record MonthTotals(string Month, decimal Income, decimal Expense);

/// <summary>One top-level category's total for a month, and its share of them all.</summary>
public sealed record CategoryTotal(Guid CategoryId, string Name, decimal Amount, decimal Share);

/// <summary>Stub: 005 checkpoint 2 implements these.</summary>
public sealed class DashboardQueries(AppDbContext database)
{
    internal const string BalancesSql = "";

    internal const string MonthlySql = "";

    internal const string ByCategorySql = "";

    private readonly AppDbContext _database = database;

    public Task<DashboardSummary> SummaryAsync(Guid userId, DateOnly month, CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<MonthTotals>> MonthlyAsync(
        Guid userId,
        DateOnly lastMonth,
        int months,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException();

    public Task<IReadOnlyList<CategoryTotal>> ByCategoryAsync(
        Guid userId,
        DateOnly month,
        CategoryKind kind,
        CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
