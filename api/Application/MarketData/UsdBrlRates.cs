using Finance.Api.Domain.MarketData;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Application.MarketData;

/// <summary>
/// The USDBRL rates a USD asset's history converts at (007's FX rule): each date takes the
/// latest rate on or before it, or the earliest one after it when none precedes, so the
/// load starts at the latest rate on or before the first date that needs one.
/// </summary>
internal static class UsdBrlRates
{
    /// <summary>
    /// USDBRL from the latest rate on or before <paramref name="first"/> (from the earliest
    /// when none is), through <paramref name="last"/> when given, oldest first.
    /// </summary>
    public static async Task<List<Benchmark>> UsdBrlRatesAsync(
        this AppDbContext db, DateOnly first, DateOnly? last, CancellationToken cancellationToken)
    {
        var rates = db.Benchmarks.AsNoTracking().Where(rate => rate.Code == Benchmark.UsdBrl);
        if (last is { } end)
        {
            rates = rates.Where(rate => rate.Date <= end);
        }

        var anchor = await rates.Where(rate => rate.Date <= first).MaxAsync(rate => (DateOnly?)rate.Date, cancellationToken);
        return await (anchor is { } start ? rates.Where(rate => rate.Date >= start) : rates)
            .OrderBy(rate => rate.Date).ToListAsync(cancellationToken);
    }
}
