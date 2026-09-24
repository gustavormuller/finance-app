using System.Text.Encodings.Web;
using System.Text.Json;
using Finance.Api.Application.Dashboard;
using Finance.Api.Application.Investments;
using Finance.Api.Domain.Transactions;

namespace Finance.Api.Application.Ai;

/// <summary>One top-level category's total in one month, signed as stored (an expense is negative).</summary>
public sealed record AnalysisCategoryMonth(string Month, Guid CategoryId, string Name, CategoryKind Kind, decimal Amount);

/// <summary>A merchant (a normalized description) and what was spent there in the analysed month, as a positive amount.</summary>
public sealed record AnalysisMerchant(string Name, decimal Spent, int Transactions);

/// <summary>Everything decision 7 lets the analysis see, before it is written as JSON.</summary>
public sealed record AnalysisAggregates(
    string Month,
    IReadOnlyList<MonthTotals> Months,
    IReadOnlyList<AnalysisCategoryMonth> Categories,
    IReadOnlyList<AnalysisMerchant> Merchants,
    IReadOnlyList<AccountBalance> Accounts,
    decimal BalanceTotalBrl,
    PortfolioSummary Investments);

/// <summary>
/// 009's <c>AnalysisInputBuilder</c>: decision 7's aggregates as one JSON document, the
/// analysis prompt's user message. Aggregates only: per-category totals and income and
/// expense for three months, the month-over-month changes, the top merchants by spend,
/// balances and 007's portfolio summary. No transaction row and no description beyond a
/// merchant's normalized name.
/// </summary>
/// <remarks>
/// The shape is pinned by a unit test (spec test 11); the prompt describes it, so changing
/// one means changing the other and bumping the prompt's version. Money is BRL only, as on
/// the dashboard, with two places. Expense is written as a positive amount spent, so the
/// model never has to reason about signs.
/// </remarks>
public static class AnalysisInputBuilder
{
    public const int MonthsCovered = 3;

    public const int TopMerchants = 20;

    private static readonly JsonSerializerOptions Json = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
    };

    public static string Build(AnalysisAggregates aggregates)
    {
        var months = aggregates.Months.OrderBy(month => month.Month, StringComparer.Ordinal).ToList();
        var keys = months.Select(month => month.Month).ToList();
        var (previous, current) = (months[^2], months[^1]);

        var categories = aggregates.Categories
            .GroupBy(category => category.CategoryId)
            .Select(group =>
            {
                var first = group.First();
                var amounts = keys.ToDictionary(
                    key => key,
                    key => Brl(Spent(first.Kind, group.Where(entry => entry.Month == key).Sum(entry => entry.Amount))));
                return new { first.Name, first.Kind, Amounts = amounts, Change = Change(amounts[previous.Month], amounts[current.Month]) };
            })
            .OrderBy(category => category.Kind == CategoryKind.Expense ? 0 : 1)
            .ThenByDescending(category => category.Amounts[current.Month])
            .ThenBy(category => category.Name, StringComparer.Ordinal)
            .Select(category => new
            {
                category.Name,
                Kind = category.Kind.ToString(),
                category.Amounts,
                category.Change.Change,
                category.Change.ChangePercent,
            });

        var document = new
        {
            aggregates.Month,
            Currency = Account.DefaultCurrency,
            Months = months.Select(month => new
            {
                month.Month,
                Income = Brl(month.Income),
                Expense = Brl(0m - month.Expense),
                Net = Brl(month.Income + month.Expense),
            }),
            MonthOverMonth = new
            {
                Income = Change(Brl(previous.Income), Brl(current.Income)),
                Expense = Change(Brl(0m - previous.Expense), Brl(0m - current.Expense)),
                Net = Change(Brl(previous.Income + previous.Expense), Brl(current.Income + current.Expense)),
            },
            Categories = categories,
            TopMerchants = aggregates.Merchants
                .OrderByDescending(merchant => merchant.Spent)
                .ThenBy(merchant => merchant.Name, StringComparer.Ordinal)
                .Take(TopMerchants)
                .Select(merchant => new { merchant.Name, Spent = Brl(merchant.Spent), merchant.Transactions }),
            Accounts = aggregates.Accounts.Select(account => new
            {
                account.Name,
                Type = account.Type.ToString(),
                account.Currency,
                Balance = Brl(account.Balance),
            }),
            BalanceTotalBrl = Brl(aggregates.BalanceTotalBrl),
            Investments = new
            {
                ValueBrl = Brl(aggregates.Investments.TotalBrl),
                CostBrl = Brl(aggregates.Investments.TotalCostBrl),
                UnrealisedBrl = Brl(aggregates.Investments.UnrealisedBrl),
            },
        };

        return JsonSerializer.Serialize(document, Json);
    }

    private sealed record MonthChange(decimal Previous, decimal Current, decimal Change, decimal? ChangePercent);

    /// <summary>
    /// The change and its percentage of the previous month's size, one place, half away from
    /// zero. No percentage from zero: it would be infinite, and a model would print it.
    /// </summary>
    private static MonthChange Change(decimal previous, decimal current)
    {
        var change = current - previous;
        decimal? percent = previous == 0m
            ? null
            : Math.Round(change / Math.Abs(previous) * 100m, 1, MidpointRounding.AwayFromZero) + 0.0m;
        return new MonthChange(previous, current, change, percent);
    }

    /// <summary>An expense category's total as the positive amount spent; income as it is.</summary>
    private static decimal Spent(CategoryKind kind, decimal stored) => kind == CategoryKind.Expense ? 0m - stored : stored;

    /// <summary>Two places always, so every amount reads alike ("0.00", not "0").</summary>
    private static decimal Brl(decimal amount) => Math.Round(amount, 2, MidpointRounding.AwayFromZero) + 0.00m;
}
