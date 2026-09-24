using Dapper;
using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Application.Dashboard;

/// <summary>One account on the dashboard, with its current balance.</summary>
/// <remarks>
/// <c>ExcludedFromTotal</c> is true for every account not in BRL: listed, but not
/// added to a total it is not denominated in (005, API surface).
/// </remarks>
public sealed record AccountBalance(
    Guid AccountId,
    string Name,
    AccountType Type,
    string Currency,
    decimal Balance,
    bool ExcludedFromTotal);

/// <summary>
/// Income, expense and their sum over one calendar month. Signed as stored: expense
/// is negative, so <c>Net</c> is <c>Income + Expense</c>.
/// </summary>
public sealed record MonthSummary(decimal Income, decimal Expense, decimal Net);

public sealed record DashboardSummary(
    IReadOnlyList<AccountBalance> Balances,
    decimal Total,
    MonthSummary Month);

/// <summary>One calendar month of the series, <c>Month</c> as <c>YYYY-MM</c>.</summary>
public sealed record MonthTotals(string Month, decimal Income, decimal Expense);

/// <summary>One top-level category's total for a month, and its share of them all.</summary>
public sealed record CategoryTotal(Guid CategoryId, string Name, decimal Amount, decimal Share);

/// <summary>
/// The dashboard's three aggregations (005), in SQL through Dapper on the context's
/// own connection (ARCHITECTURE.md §6).
/// </summary>
/// <remarks>
/// Dapper does not go through EF Core, so the global query filter does not apply
/// here. Every statement in this folder therefore names <c>"UserId" = @userId</c>
/// itself, and <c>DashboardSqlTests</c> fails the build when one does not.
/// <para>
/// Income and expense only ever sum BRL rows, for the same reason the balance total
/// does: adding two currencies is the bug <see cref="Money"/> exists to stop, and
/// conversion is not this feature's problem. Transfer (<c>Kind = 2</c>) is never
/// income or expense.
/// </para>
/// </remarks>
public sealed class DashboardQueries(AppDbContext database)
{
    internal const string BalancesSql = """
        SELECT a."Id" AS "AccountId", a."Name", a."Type", a."Currency",
               a."OpeningBalance" + COALESCE(SUM(t."Amount"), 0) AS "Balance"
        FROM "Accounts" a
        LEFT JOIN "Transactions" t ON t."AccountId" = a."Id" AND t."UserId" = @userId
        WHERE a."UserId" = @userId
        GROUP BY a."Id"
        ORDER BY a."Name", a."Id"
        """;

    /// <remarks>
    /// Months are computed on <c>timestamp</c>, never <c>timestamptz</c>: a
    /// <c>date</c> has no timezone, and letting PostgreSQL pick the
    /// <c>timestamptz</c> overload would put the session's clock back into it.
    /// </remarks>
    internal const string MonthlySql = """
        WITH months AS (
            SELECT (make_date(@fromYear, @fromMonth, 1)::timestamp
                    + make_interval(months => step))::date AS "Start"
            FROM generate_series(0, @count - 1) AS step
        ),
        totals AS (
            SELECT date_trunc('month', t."Date"::timestamp)::date AS "Start",
                   SUM(t."Amount") FILTER (WHERE c."Kind" = 0) AS "Income",
                   SUM(t."Amount") FILTER (WHERE c."Kind" = 1) AS "Expense"
            FROM "Transactions" t
            JOIN "Categories" c ON c."Id" = t."CategoryId"
            WHERE t."UserId" = @userId
              AND c."UserId" = @userId
              AND c."Kind" <> 2
              AND t."Currency" = @currency
              AND t."Date" >= make_date(@fromYear, @fromMonth, 1)
              AND t."Date" < (make_date(@fromYear, @fromMonth, 1)::timestamp
                              + make_interval(months => @count))::date
            GROUP BY 1
        )
        SELECT to_char(m."Start", 'YYYY-MM') AS "Month",
               COALESCE(s."Income", 0) AS "Income",
               COALESCE(s."Expense", 0) AS "Expense"
        FROM months m
        LEFT JOIN totals s ON s."Start" = m."Start"
        ORDER BY m."Start"
        """;

    /// <remarks>
    /// Categories are two levels deep at most, so one join to
    /// <c>COALESCE("ParentId", "Id")</c> reaches the top level. A child's kind always
    /// equals its parent's, so filtering on the child's kind is filtering on both.
    /// </remarks>
    internal const string ByCategorySql = """
        SELECT root."Id" AS "CategoryId", root."Name", SUM(t."Amount") AS "Amount"
        FROM "Transactions" t
        JOIN "Categories" c ON c."Id" = t."CategoryId"
        JOIN "Categories" root ON root."Id" = COALESCE(c."ParentId", c."Id")
        WHERE t."UserId" = @userId
          AND c."UserId" = @userId
          AND c."Kind" = @kind
          AND t."Currency" = @currency
          AND t."Date" >= make_date(@year, @month, 1)
          AND t."Date" < (make_date(@year, @month, 1)::timestamp + interval '1 month')::date
        GROUP BY root."Id", root."Name"
        ORDER BY abs(SUM(t."Amount")) DESC, root."Name"
        """;

    private sealed record BalanceRow(Guid AccountId, string Name, AccountType Type, string Currency, decimal Balance);

    private sealed record CategoryRow(Guid CategoryId, string Name, decimal Amount);

    public async Task<DashboardSummary> SummaryAsync(Guid userId, DateOnly month, CancellationToken cancellationToken)
    {
        var rows = await QueryAsync<BalanceRow>(BalancesSql, new { userId }, cancellationToken);

        var balances = rows
            .Select(row => new AccountBalance(
                row.AccountId,
                row.Name,
                row.Type,
                row.Currency,
                row.Balance,
                ExcludedFromTotal: row.Currency != Account.DefaultCurrency))
            .ToList();

        var totals = (await MonthlyAsync(userId, month, 1, cancellationToken)).Single();

        return new DashboardSummary(
            balances,
            balances.Where(balance => !balance.ExcludedFromTotal).Sum(balance => balance.Balance),
            new MonthSummary(totals.Income, totals.Expense, totals.Income + totals.Expense));
    }

    /// <summary>The <paramref name="months"/> calendar months ending with <paramref name="lastMonth"/>, oldest first.</summary>
    public async Task<IReadOnlyList<MonthTotals>> MonthlyAsync(
        Guid userId,
        DateOnly lastMonth,
        int months,
        CancellationToken cancellationToken)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(months, 1);

        var first = lastMonth.AddMonths(1 - months);

        return await QueryAsync<MonthTotals>(
            MonthlySql,
            new
            {
                userId,
                fromYear = first.Year,
                fromMonth = first.Month,
                count = months,
                currency = Account.DefaultCurrency,
            },
            cancellationToken);
    }

    public async Task<IReadOnlyList<CategoryTotal>> ByCategoryAsync(
        Guid userId,
        DateOnly month,
        CategoryKind kind,
        CancellationToken cancellationToken)
    {
        if (kind == CategoryKind.Transfer)
        {
            throw new ArgumentException("Transfers are neither income nor expense.", nameof(kind));
        }

        var rows = await QueryAsync<CategoryRow>(
            ByCategorySql,
            new
            {
                userId,
                year = month.Year,
                month = month.Month,
                kind = (int)kind,
                currency = Account.DefaultCurrency,
            },
            cancellationToken);

        var shares = Shares.Of([.. rows.Select(row => row.Amount)]);

        return [.. rows.Select((row, index) => new CategoryTotal(row.CategoryId, row.Name, row.Amount, shares[index]))];
    }

    private async Task<IReadOnlyList<T>> QueryAsync<T>(string sql, object parameters, CancellationToken cancellationToken) =>
        [.. await database.Database.GetDbConnection().QueryAsync<T>(
            new CommandDefinition(sql, parameters, cancellationToken: cancellationToken))];
}
