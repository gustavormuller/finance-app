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

/// <summary>009's <c>AnalysisInputBuilder</c>: decision 7's aggregates as one JSON document.</summary>
public static class AnalysisInputBuilder
{
    public const int MonthsCovered = 3;

    public const int TopMerchants = 20;

    public static string Build(AnalysisAggregates aggregates) => throw new NotImplementedException();
}
