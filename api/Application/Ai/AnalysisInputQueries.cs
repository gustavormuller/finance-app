using System.Globalization;
using Finance.Api.Application.Dashboard;
using Finance.Api.Application.Investments;
using Finance.Api.Domain.Import;
using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Application.Ai;

/// <summary>
/// Loads <see cref="AnalysisAggregates"/> for the scope's user: the request's, or the user the
/// analysis job's scope acts for (<see cref="ActingUser"/>). The totals and balances are
/// 005's dashboard queries (each names the user itself), the portfolio is 007's summary only
/// (the user's decision: not 008's returns), and the merchants go through the query
/// filter.
/// </summary>
public sealed class AnalysisInputQueries(
    AppDbContext db, DashboardQueries dashboard, PositionQueries positions, ICurrentUser currentUser)
{
    /// <param name="month">The month analysed, <c>YYYY-MM</c>, already validated.</param>
    public async Task<AnalysisAggregates> AggregatesAsync(string month, CancellationToken ct)
    {
        var userId = currentUser.Id ?? throw new InvalidOperationException("The analysis input needs a user.");
        var first = DateOnly.ParseExact(month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);

        var months = await dashboard.MonthlyAsync(userId, first, AnalysisInputBuilder.MonthsCovered, ct);
        var categories = new List<AnalysisCategoryMonth>();
        foreach (var totals in months)
        {
            var start = DateOnly.ParseExact(totals.Month + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture);
            foreach (var kind in new[] { CategoryKind.Expense, CategoryKind.Income })
            {
                categories.AddRange((await dashboard.ByCategoryAsync(userId, start, kind, ct))
                    .Select(total => new AnalysisCategoryMonth(totals.Month, total.CategoryId, total.Name, kind, total.Amount)));
            }
        }

        var summary = await dashboard.SummaryAsync(userId, first, ct);
        return new AnalysisAggregates(
            month,
            months,
            categories,
            await MerchantsAsync(first, ct),
            summary.Balances,
            summary.Total,
            await positions.SummaryAsync(ct));
    }

    /// <summary>
    /// The month's BRL expense, by merchant. A merchant is the row's normalized description
    /// (digits, dates and ids stripped), or the description normalized here for a row
    /// entered by hand. Income is never listed: its descriptions name payers.
    /// </summary>
    private async Task<IReadOnlyList<AnalysisMerchant>> MerchantsAsync(DateOnly first, CancellationToken ct)
    {
        var next = first.AddMonths(1);
        var rows = await (
                from transaction in db.Transactions
                join category in db.Categories on transaction.CategoryId equals category.Id
                where category.Kind == CategoryKind.Expense
                      && transaction.Date >= first && transaction.Date < next
                      && transaction.Money.Currency == Account.DefaultCurrency
                select new { transaction.NormalizedDescription, transaction.Description, transaction.Money.Amount })
            .AsNoTracking()
            .ToListAsync(ct);

        return [.. rows
            .GroupBy(row => row.NormalizedDescription ?? DescriptionNormalizer.Normalize(row.Description))
            .Where(group => group.Key.Length > 0)
            .Select(group => new AnalysisMerchant(group.Key, 0m - group.Sum(row => row.Amount), group.Count()))
            .OrderByDescending(merchant => merchant.Spent)
            .ThenBy(merchant => merchant.Name, StringComparer.Ordinal)
            .Take(AnalysisInputBuilder.TopMerchants)];
    }
}
