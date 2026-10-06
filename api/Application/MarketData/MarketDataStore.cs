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
/// The batch travels as arrays unnested server-side: five years of closes are one
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
        var lastPerDay = LastPerDay(closes, close => close.Date);
        DateOnly[] dates = [.. lastPerDay.Select(close => close.Date)];
        decimal[] values = [.. lastPerDay.Select(close => close.Close)];
        decimal?[] adjusted = [.. lastPerDay.Select(close => close.AdjustedClose)];

        return dates.Length == 0
            ? Task.FromResult(0)
            : db.Database.ExecuteSqlAsync(
                $"""
                INSERT INTO "Prices" ("MarketAssetId", "Date", "Close", "AdjustedClose")
                SELECT {marketAssetId}, point.day, point.value, point.adjusted
                FROM unnest({dates}, {values}, {adjusted}) AS point(day, value, adjusted)
                ON CONFLICT ("MarketAssetId", "Date")
                DO UPDATE SET "Close" = EXCLUDED."Close", "AdjustedClose" = EXCLUDED."AdjustedClose"
                """,
                cancellationToken);
    }

    /// <summary>
    /// Deletes every stored close of the asset and writes <paramref name="closes"/> in their
    /// place, in the caller's transaction: a series from a new source is never merged with
    /// the old one's (025).
    /// </summary>
    /// <returns>The rows written.</returns>
    public async Task<int> ReplacePricesAsync(
        Guid marketAssetId, IReadOnlyList<DailyClose> closes, CancellationToken cancellationToken)
    {
        await db.Set<Domain.MarketData.Price>().Where(price => price.MarketAssetId == marketAssetId)
            .ExecuteDeleteAsync(cancellationToken);
        return await UpsertPricesAsync(marketAssetId, closes, cancellationToken);
    }

    /// <returns>The rows inserted or overwritten.</returns>
    public Task<int> UpsertBenchmarkAsync(
        string code, IReadOnlyList<DailyValue> values, CancellationToken cancellationToken)
    {
        var lastPerDay = LastPerDay(values, value => value.Date);
        DateOnly[] dates = [.. lastPerDay.Select(value => value.Date)];
        decimal[] amounts = [.. lastPerDay.Select(value => value.Value)];

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

    private static List<T> LastPerDay<T>(IEnumerable<T> points, Func<T, DateOnly> date)
    {
        var lastPerDay = new SortedDictionary<DateOnly, T>();
        foreach (var point in points)
        {
            lastPerDay[date(point)] = point;
        }

        return [.. lastPerDay.Values];
    }
}
