using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Application.MarketData;

/// <summary>
/// Writes provider series into the shared <c>Prices</c> and <c>Benchmarks</c> tables:
/// one <c>INSERT ... ON CONFLICT DO UPDATE</c> per batch, so a re-sync of a stored day
/// overwrites it (006, test 18). Shared data, so the SQL carries no user predicate,
/// and it lives outside <c>Application/Dashboard/</c> whose scan demands one.
/// </summary>
/// <remarks>
/// The batch travels as two arrays unnested server-side: five years of closes are one
/// round trip, not 1250. PostgreSQL refuses to update one row twice in a statement,
/// and a provider may report a day twice (CoinGecko's last point is "now"), so each
/// day keeps the last value it was given. Values beyond eight decimals are rounded by
/// <c>numeric(18,8)</c>.
/// </remarks>
public sealed class MarketDataStore(AppDbContext db)
{
    /// <returns>The rows inserted or overwritten.</returns>
    public Task<int> UpsertPricesAsync(
        Guid marketAssetId, IReadOnlyList<DailyClose> closes, CancellationToken cancellationToken)
    {
        var (dates, values) = LastPerDay(closes.Select(close => (close.Date, close.Close)));

        return dates.Length == 0
            ? Task.FromResult(0)
            : db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO "Prices" ("MarketAssetId", "Date", "Close")
                SELECT {marketAssetId}, point.day, point.value
                FROM unnest({dates}, {values}) AS point(day, value)
                ON CONFLICT ("MarketAssetId", "Date") DO UPDATE SET "Close" = EXCLUDED."Close"
                """,
                cancellationToken);
    }

    /// <returns>The rows inserted or overwritten.</returns>
    public Task<int> UpsertBenchmarkAsync(
        string code, IReadOnlyList<DailyValue> values, CancellationToken cancellationToken)
    {
        var (dates, amounts) = LastPerDay(values.Select(value => (value.Date, value.Value)));

        return dates.Length == 0
            ? Task.FromResult(0)
            : db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO "Benchmarks" ("Code", "Date", "Value")
                SELECT {code}, point.day, point.value
                FROM unnest({dates}, {amounts}) AS point(day, value)
                ON CONFLICT ("Code", "Date") DO UPDATE SET "Value" = EXCLUDED."Value"
                """,
                cancellationToken);
    }

    private static (DateOnly[] Dates, decimal[] Values) LastPerDay(IEnumerable<(DateOnly Date, decimal Value)> points)
    {
        var lastPerDay = new SortedDictionary<DateOnly, decimal>();
        foreach (var (date, value) in points)
        {
            lastPerDay[date] = value;
        }

        return ([.. lastPerDay.Keys], [.. lastPerDay.Values]);
    }
}
