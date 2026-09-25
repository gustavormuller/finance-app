using Finance.Api.Application.Dashboard;
using Finance.Api.Application.MarketData;
using Finance.Api.Domain.Investments;
using Finance.Api.Domain.MarketData;
using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Application.Investments;

/// <summary>One held asset, as <c>GET /api/investments/assets</c> answers it (007, "Position shape").</summary>
/// <remarks>
/// The valuation (<c>Price</c> through <c>UnrealisedPct</c>) is the latest
/// <see cref="PortfolioDaily"/> row, and is <c>null</c> while the asset has none: no
/// movement yet, or no close yet. Quantity, average cost, realised gain and dividends come
/// from <see cref="PositionCalculator"/> over the movements, never from the rounded rows.
/// </remarks>
public sealed record PositionView(
    Guid AssetId,
    string Ticker,
    string Name,
    MarketAssetClass Class,
    string Currency,
    string? Nickname,
    decimal Quantity,
    decimal AverageCost,
    decimal? Price,
    DateOnly? PriceDate,
    decimal? ValueBrl,
    decimal? CostBasisBrl,
    decimal? UnrealisedBrl,
    decimal? UnrealisedPct,
    decimal? RealisedBrl,
    decimal? DividendsBrl);

/// <summary><c>GET /api/investments/summary</c>: the latest daily row of every held asset, added up.</summary>
/// <remarks>
/// 016 adds the allocation by class and the latest USDBRL, which the screens convert with.
/// They are init-only so 009's analysis input, which reads the totals alone, builds one as before.
/// </remarks>
public sealed record PortfolioSummary(decimal TotalBrl, decimal TotalCostBrl, decimal UnrealisedBrl)
{
    /// <summary>By value, highest first; a class worth zero is left out. Shares sum to exactly 1.</summary>
    public IReadOnlyList<AllocationView> Allocation { get; init; } = [];

    /// <summary>The latest stored USDBRL row, shared market data; <c>null</c> when none was ever synced.</summary>
    public UsdBrlView? UsdBrl { get; init; }
}

/// <summary>One asset class's part of the summary's total.</summary>
public sealed record AllocationView(MarketAssetClass Class, decimal ValueBrl, decimal Share);

/// <summary>BRL per US dollar, and the day of that rate.</summary>
public sealed record UsdBrlView(decimal Rate, DateOnly Date);

/// <summary>The current user's positions: movements through the calculator, valued by the latest daily row.</summary>
public sealed class PositionQueries(AppDbContext db)
{
    /// <summary>The column's scale, and the rounding <see cref="SnapshotBuilder"/> writes it with.</summary>
    private const int AverageCostPlaces = 8;

    private const int PercentPlaces = 4;

    /// <summary>Every held asset, or only <paramref name="assetId"/>; by value, highest first, then ticker.</summary>
    public async Task<IReadOnlyList<PositionView>> ListAsync(CancellationToken cancellationToken, Guid? assetId = null)
    {
        var assets = await (
                from held in db.Assets
                join market in db.MarketAssets on held.MarketAssetId equals market.Id
                where assetId == null || held.Id == assetId
                select new { held.Id, held.Nickname, market.Ticker, market.Name, market.Class, market.Currency })
            .ToListAsync(cancellationToken);
        var ids = assets.Select(asset => asset.Id).ToList();

        var movements = (await db.Movements.AsNoTracking().Where(movement => ids.Contains(movement.AssetId))
                .ToListAsync(cancellationToken))
            .ToLookup(movement => movement.AssetId);
        var latest = await db.PortfolioDaily.AsNoTracking()
            .Where(row => ids.Contains(row.AssetId)
                && row.Date == db.PortfolioDaily.Where(other => other.AssetId == row.AssetId).Max(other => other.Date))
            .ToDictionaryAsync(row => row.AssetId, cancellationToken);
        var rates = await RatesAsync(
            assets.Where(asset => asset.Currency != SnapshotBuilder.BaseCurrency).SelectMany(asset => movements[asset.Id]),
            cancellationToken);

        return [.. assets
            .Select(asset =>
            {
                var (position, realised, income) = Replay(movements[asset.Id], asset.Currency, rates);
                var row = latest.GetValueOrDefault(asset.Id);
                var unrealised = row is null ? (decimal?)null : row.ValueBrl - row.CostBasisBrl;
                return new PositionView(
                    asset.Id, asset.Ticker, asset.Name, asset.Class, asset.Currency, asset.Nickname,
                    position.Quantity,
                    Math.Round(position.AverageCost, AverageCostPlaces, MidpointRounding.ToEven),
                    row?.Price, row?.PriceDate, row?.ValueBrl, row?.CostBasisBrl, unrealised,
                    row is { CostBasisBrl: > 0m } ? Math.Round(unrealised!.Value / row.CostBasisBrl, PercentPlaces, MidpointRounding.ToEven) : null,
                    realised, income);
            })
            .OrderByDescending(position => position.ValueBrl ?? 0m)
            .ThenBy(position => position.Ticker, StringComparer.Ordinal)];
    }

    /// <summary>
    /// Sums the latest <see cref="PortfolioDaily"/> row of each asset (spec test 26). An
    /// asset with no row yet adds nothing, and so does a position sold down to zero. The
    /// same rows, by class, are the allocation.
    /// </summary>
    public async Task<PortfolioSummary> SummaryAsync(CancellationToken cancellationToken)
    {
        var latest = await (
                from row in db.PortfolioDaily.AsNoTracking()
                where row.Date == db.PortfolioDaily.Where(other => other.AssetId == row.AssetId).Max(other => other.Date)
                join held in db.Assets on row.AssetId equals held.Id
                join market in db.MarketAssets on held.MarketAssetId equals market.Id
                select new { row.ValueBrl, row.CostBasisBrl, market.Class })
            .ToListAsync(cancellationToken);
        var total = latest.Sum(row => row.ValueBrl);
        var cost = latest.Sum(row => row.CostBasisBrl);

        var classes = latest.GroupBy(row => row.Class)
            .Select(group => (Class: group.Key, Value: group.Sum(row => row.ValueBrl)))
            .Where(item => item.Value > 0m)
            .OrderByDescending(item => item.Value)
            .ThenBy(item => item.Class)
            .ToList();
        var shares = Shares.Of([.. classes.Select(item => item.Value)]);

        var usdBrl = await db.Benchmarks.AsNoTracking()
            .Where(rate => rate.Code == Benchmark.UsdBrl)
            .OrderByDescending(rate => rate.Date)
            .Select(rate => new UsdBrlView(rate.Value, rate.Date))
            .FirstOrDefaultAsync(cancellationToken);

        return new PortfolioSummary(total, cost, total - cost)
        {
            Allocation = [.. classes.Select((item, index) => new AllocationView(item.Class, item.Value, shares[index]))],
            UsdBrl = usdBrl,
        };
    }

    /// <summary>
    /// The position after every movement, and realised gain and dividends in BRL. For a
    /// non-BRL asset each event is converted at the USDBRL rate of its own date (the latest
    /// on or before it, or the earliest after it, as <see cref="SnapshotBuilder"/> costs a
    /// buy), and both are <c>null</c> when there is no rate at all.
    /// </summary>
    private static (Position Position, decimal? Realised, decimal? Income) Replay(
        IEnumerable<Movement> movements, string currency, IReadOnlyList<Benchmark> rates)
    {
        var steps = PositionCalculator.Calculate(movements);
        var last = steps.Count == 0 ? default : steps[^1].Position;
        if (currency == SnapshotBuilder.BaseCurrency)
        {
            return (last, Brl(last.RealisedGain), Brl(last.Income));
        }

        if (rates.Count == 0)
        {
            return (last, null, null);
        }

        decimal realised = 0m, income = 0m;
        var before = default(Position);
        foreach (var step in steps)
        {
            var rate = (rates.LastOrDefault(value => value.Date <= step.Movement.Date) ?? rates[0]).Value;
            realised += (step.Position.RealisedGain - before.RealisedGain) * rate;
            income += (step.Position.Income - before.Income) * rate;
            before = step.Position;
        }

        return (last, Brl(realised), Brl(income));
    }

    /// <summary>USDBRL from the latest rate on or before the earliest of <paramref name="movements"/>, oldest first.</summary>
    private async Task<List<Benchmark>> RatesAsync(IEnumerable<Movement> movements, CancellationToken cancellationToken)
    {
        var first = movements.Select(movement => (DateOnly?)movement.Date).Min();
        return first is { } date ? await db.UsdBrlRatesAsync(date, last: null, cancellationToken) : [];
    }

    private static decimal Brl(decimal amount) => new Money(amount, SnapshotBuilder.BaseCurrency).Amount;
}
