using Finance.Api.Domain.Investments;
using Finance.Api.Domain.MarketData;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Application.Investments;

/// <summary>
/// The spec's <c>RebuildSnapshots(userId, assetId, fromDate)</c>: deletes an asset's
/// <see cref="PortfolioDaily"/> rows from a date, builds them again with
/// <see cref="SnapshotBuilder"/> through today, and inserts them, in one database
/// transaction (ADR-011; spec test 22). The user is the one the context is filtered by.
/// </summary>
/// <remarks>
/// <para>
/// Joins the caller's transaction when there is one, so a movement write and its rebuild
/// commit together; otherwise it opens its own.
/// </para>
/// <para>
/// Loads what <see cref="SnapshotBuilder.Build"/> needs: the <b>whole</b> movement history,
/// the closes from the latest one on or before <c>from</c>, and the USDBRL rates from the
/// latest one on or before the first movement, which covers the cost of every buy.
/// "Today" is the UTC date, the day the sync's "yesterday" is counted from.
/// </para>
/// <para>
/// A transaction-scoped advisory lock on the asset serialises two rebuilds of it (a
/// movement write and the nightly run); without it, both would delete, then both insert,
/// and one would fail on the primary key.
/// </para>
/// </remarks>
public sealed class SnapshotRebuild(AppDbContext db, TimeProvider clock)
{
    /// <summary>The benchmark series that values a USD asset in BRL.</summary>
    public const string UsdBrl = "USDBRL";

    public DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    /// <summary>
    /// Every asset of the current user, from its first movement, each in its own
    /// transaction (<c>POST /api/investments/rebuild</c>). Returns the assets and rows.
    /// </summary>
    public async Task<(int Assets, int Rows)> RebuildAllAsync(CancellationToken cancellationToken)
    {
        var ids = await db.Assets.Select(asset => asset.Id).ToListAsync(cancellationToken);
        var rows = 0;
        foreach (var id in ids)
        {
            // No movement or row can predate the rule's lower bound.
            rows += await RebuildAsync(id, MovementRules.MinimumDate, cancellationToken);
        }

        return (ids.Count, rows);
    }

    /// <summary>Rebuilds one asset from <paramref name="from"/>; returns the rows written.</summary>
    /// <exception cref="InvalidOperationException">The asset is not the current user's.</exception>
    public async Task<int> RebuildAsync(Guid assetId, DateOnly from, CancellationToken cancellationToken)
    {
        var asset = await (
                from held in db.Assets
                join market in db.MarketAssets on held.MarketAssetId equals market.Id
                where held.Id == assetId
                select new { held.UserId, held.MarketAssetId, market.Currency })
            .SingleAsync(cancellationToken);

        await using var owned = db.Database.CurrentTransaction is null
            ? await db.Database.BeginTransactionAsync(cancellationToken)
            : null;

        await db.Database.ExecuteSqlAsync($"SELECT pg_advisory_xact_lock({LockKey(assetId)})", cancellationToken);

        var movements = await db.Movements.AsNoTracking()
            .Where(movement => movement.AssetId == assetId).ToListAsync(cancellationToken);
        var prices = await ClosesAsync(asset.MarketAssetId, from, cancellationToken);
        var rates = asset.Currency == SnapshotBuilder.BaseCurrency || movements.Count == 0
            ? []
            : await RatesAsync(movements.Min(movement => movement.Date), cancellationToken);

        await db.PortfolioDaily.Where(row => row.AssetId == assetId && row.Date >= from).ExecuteDeleteAsync(cancellationToken);

        var rows = SnapshotBuilder.Build(asset.UserId, assetId, asset.Currency, movements, prices, rates, from, Today);
        await InsertAsync(asset.UserId, assetId, rows, cancellationToken);

        if (owned is not null)
        {
            await owned.CommitAsync(cancellationToken);
        }

        return rows.Count;
    }

    /// <summary>
    /// Where the nightly rebuild starts for an asset: <paramref name="yesterday"/>, the day
    /// the sync just wrote, or earlier when rows are missing before it. Rows stop early
    /// when the job missed nights; they start late, or not at all, when the closes (or
    /// USDBRL rates) were backfilled after the movements were written.
    /// </summary>
    public async Task<DateOnly> NightlyFromAsync(Guid assetId, DateOnly yesterday, CancellationToken cancellationToken)
    {
        var asset = await (
                from held in db.Assets
                join market in db.MarketAssets on held.MarketAssetId equals market.Id
                where held.Id == assetId
                select new { held.MarketAssetId, market.Currency })
            .SingleAsync(cancellationToken);
        var rows = db.PortfolioDaily.Where(row => row.AssetId == assetId);
        var firstRow = await rows.MinAsync(row => (DateOnly?)row.Date, cancellationToken);
        var lastRow = await rows.MaxAsync(row => (DateOnly?)row.Date, cancellationToken);

        // The first day SnapshotBuilder can write: a movement, a close and, unless BRL, a rate.
        DateOnly?[] starts =
        [
            await db.Movements.Where(movement => movement.AssetId == assetId).MinAsync(movement => (DateOnly?)movement.Date, cancellationToken),
            await db.Prices.Where(price => price.MarketAssetId == asset.MarketAssetId).MinAsync(price => (DateOnly?)price.Date, cancellationToken),
            asset.Currency == SnapshotBuilder.BaseCurrency
                ? DateOnly.MinValue
                : await db.Benchmarks.Where(rate => rate.Code == UsdBrl).MinAsync(rate => (DateOnly?)rate.Date, cancellationToken),
        ];

        var from = lastRow is { } last && last < yesterday ? last.AddDays(1) : yesterday;
        if (starts.All(start => start is not null) && starts.Max() is { } expected && (firstRow is null || firstRow > expected) && expected < from)
        {
            from = expected;
        }

        return from;
    }

    /// <summary>
    /// One statement for all of an asset's rows, in the transaction the rebuild runs in.
    /// Through <c>SaveChanges</c>, five years of one asset (about 1 800 rows) were 1 800
    /// tracked entities and 18 000 parameters, and nine tenths of the rebuild's time. The
    /// values are the builder's, already rounded; the columns' scales are the same either way.
    /// </summary>
    private async Task InsertAsync(Guid userId, Guid assetId, IReadOnlyList<PortfolioDaily> rows, CancellationToken cancellationToken)
    {
        if (rows.Count == 0)
        {
            return;
        }

        DateOnly[] dates = [.. rows.Select(row => row.Date)], priceDates = [.. rows.Select(row => row.PriceDate)];
        decimal[] quantities = [.. rows.Select(row => row.Quantity)], averageCosts = [.. rows.Select(row => row.AverageCost)],
            prices = [.. rows.Select(row => row.Price)], fxRates = [.. rows.Select(row => row.FxRate)],
            values = [.. rows.Select(row => row.ValueBrl)], costs = [.. rows.Select(row => row.CostBasisBrl)];

        await db.Database.ExecuteSqlAsync(
            $"""
            INSERT INTO "PortfolioDaily"
                ("UserId", "AssetId", "Date", "Quantity", "AverageCost", "Price", "PriceDate", "FxRate", "ValueBrl", "CostBasisBrl")
            SELECT {userId}, {assetId}, day.*
            FROM unnest({dates}, {quantities}, {averageCosts}, {prices}, {priceDates}, {fxRates}, {values}, {costs}) AS day
            """,
            cancellationToken);
    }

    private async Task<List<Price>> ClosesAsync(Guid marketAssetId, DateOnly from, CancellationToken cancellationToken)
    {
        var closes = db.Prices.AsNoTracking().Where(price => price.MarketAssetId == marketAssetId);
        var anchor = await closes.Where(price => price.Date <= from).MaxAsync(price => (DateOnly?)price.Date, cancellationToken);
        return await closes.Where(price => price.Date >= (anchor ?? from)).ToListAsync(cancellationToken);
    }

    private async Task<List<Benchmark>> RatesAsync(DateOnly firstMovement, CancellationToken cancellationToken)
    {
        var rates = db.Benchmarks.AsNoTracking().Where(rate => rate.Code == UsdBrl);
        var anchor = await rates.Where(rate => rate.Date <= firstMovement).MaxAsync(rate => (DateOnly?)rate.Date, cancellationToken);
        return await (anchor is { } start ? rates.Where(rate => rate.Date >= start) : rates).ToListAsync(cancellationToken);
    }

    /// <summary>The asset's id folded to the <c>bigint</c> an advisory lock takes.</summary>
    private static long LockKey(Guid assetId) => BitConverter.ToInt64(assetId.ToByteArray(), 0);
}
