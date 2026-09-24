using Finance.Api.Domain.Investments;
using Finance.Api.Domain.Returns;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Finance.Api.Application.Returns;

/// <summary>What <c>GET /api/returns</c> was asked for: a period, and its dates when custom.</summary>
public sealed record ReturnsQuery(ReturnsPeriodKind Kind, DateOnly? From, DateOnly? To);

public sealed record PeriodView(DateOnly From, DateOnly To, int Days);

public sealed record TwrView(decimal Total, decimal? Annualised);

public sealed record BenchmarkView(decimal Total, decimal? Annualised);

public sealed record FxView(decimal Native, decimal Fx, decimal Total);

/// <summary>
/// 008's response. Everything is <c>null</c> and <c>Series</c> empty when nothing was held
/// in the period (spec test 27). <c>Benchmarks</c> is keyed by code; the labels shown on
/// screen live in the web client.
/// </summary>
public sealed record ReturnsView(
    PeriodView? Period,
    TwrView? Twr,
    decimal? Xirr,
    decimal? TimingEffect,
    IReadOnlyDictionary<string, BenchmarkView?> Benchmarks,
    IReadOnlyList<IReadOnlyDictionary<string, object>> Series);

/// <summary>One asset's returns: <see cref="ReturnsView"/> and its FX split, <c>null</c> for a BRL asset.</summary>
public sealed record AssetReturnsView(
    PeriodView? Period,
    TwrView? Twr,
    decimal? Xirr,
    decimal? TimingEffect,
    FxView? Fx,
    IReadOnlyDictionary<string, BenchmarkView?> Benchmarks,
    IReadOnlyList<IReadOnlyDictionary<string, object>> Series);

/// <summary>
/// 008's reads: the current user's movements and daily rows, through the domain's TWR,
/// XIRR and timing effect (spec "API surface"). Everything read here but the benchmarks
/// is user-owned and filtered (ADR-007).
/// </summary>
/// <remarks>
/// The TWR, the benchmarks and the chart share one base day: the first daily row in
/// <c>[from - 1, to]</c>. That is <c>from - 1</c> whenever something was held then, and the
/// first contribution's day at inception, where the architecture puts base 100. XIRR opens
/// on <c>from - 1</c> with each asset's value there (zero at inception), so the first
/// day's cash counts (DEFERRED, 008 CP1 and CP4).
/// </remarks>
public sealed class ReturnsQueries(AppDbContext db, TimeProvider clock, IOptions<ReturnsOptions> options)
{
    private const int RatePlaces = 10;
    private const int IndexPlaces = 6;

    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public async Task<ReturnsView> PortfolioAsync(ReturnsQuery query, CancellationToken cancellationToken) =>
        View(await MeasureAsync(null, query, cancellationToken));

    /// <summary>One asset's returns, or <c>null</c> when it is not the current user's.</summary>
    public async Task<AssetReturnsView?> AssetAsync(Guid assetId, ReturnsQuery query, CancellationToken cancellationToken)
    {
        if (!await db.Assets.AnyAsync(asset => asset.Id == assetId, cancellationToken))
        {
            return null;
        }

        var measured = await MeasureAsync(assetId, query, cancellationToken);
        var view = View(measured);
        var fx = measured?.Fx is { } split ? new FxView(Round(split.Native), Round(split.Fx), Round(split.Total)) : null;
        return new AssetReturnsView(view.Period, view.Twr, view.Xirr, view.TimingEffect, fx, view.Benchmarks, view.Series);
    }

    private ReturnsView View(Measured? measured)
    {
        var benchmarks = options.Value.Benchmarks.Keys.Order(StringComparer.Ordinal).ToDictionary(code => code, _ => (BenchmarkView?)null);
        if (measured is null)
        {
            return new ReturnsView(null, null, null, null, benchmarks, []);
        }

        var twr = measured.Twr;
        var baseDay = twr.Index[0].Date;
        var days = measured.Range.To.DayNumber - baseDay.DayNumber;
        foreach (var (code, accumulated) in measured.Benchmarks)
        {
            var growth = accumulated[^1].Value / 100m;
            benchmarks[code] = new BenchmarkView(Round(growth - 1m), Round(TimeWeightedReturn.Annualise(growth, days)));
        }

        var portfolio = twr.Index.ToDictionary(point => point.Date, point => point.Value);
        var grid = Enumerable.Range(0, measured.Range.To.DayNumber - baseDay.DayNumber + 1).Select(baseDay.AddDays).ToList();

        var series = new List<IReadOnlyDictionary<string, object>>();
        var index = 100m;
        var kept = SeriesSampling.Positions(grid.Count, SeriesSampling.MaximumPoints).ToHashSet();
        for (var position = 0; position < grid.Count; position++)
        {
            index = portfolio.GetValueOrDefault(grid[position], index);
            if (kept.Contains(position))
            {
                var point = new Dictionary<string, object> { ["date"] = grid[position], ["portfolio"] = Round(index, IndexPlaces) };
                foreach (var (code, benchmark) in measured.Benchmarks)
                {
                    point[code] = Round(benchmark[position].Value, IndexPlaces);
                }

                series.Add(point);
            }
        }

        return new ReturnsView(
            new PeriodView(measured.Range.From, measured.Range.To, days),
            new TwrView(Round(twr.Total), Round(twr.Annualised)),
            Round(measured.Xirr),
            Round(TimingEffect.Of(measured.Xirr, twr.Annualised)),
            benchmarks,
            series);
    }

    /// <summary>The period, TWR and XIRR of one asset or the whole portfolio; <c>null</c> when nothing was held.</summary>
    private async Task<Measured?> MeasureAsync(Guid? assetId, ReturnsQuery query, CancellationToken cancellationToken)
    {
        var assets = await (
                from held in db.Assets
                join market in db.MarketAssets on held.MarketAssetId equals market.Id
                where assetId == null || held.Id == assetId
                select new { held.Id, market.Currency })
            .ToListAsync(cancellationToken);
        var ids = assets.Select(asset => asset.Id).ToList();
        var movements = (await db.Movements.AsNoTracking().Where(movement => ids.Contains(movement.AssetId))
                .ToListAsync(cancellationToken))
            .ToLookup(movement => movement.AssetId);
        var lastRow = await db.PortfolioDaily.Where(row => ids.Contains(row.AssetId))
            .MaxAsync(row => (DateOnly?)row.Date, cancellationToken);
        if (movements.Count == 0 || lastRow is null)
        {
            return null;
        }

        var firstMovement = movements.SelectMany(group => group).Min(movement => movement.Date);
        if (ReturnsPeriods.Resolve(query.Kind, query.From, query.To, Today, firstMovement, lastRow.Value) is not { } range)
        {
            return null;
        }

        var opening = range.From.AddDays(-1);
        var rows = (await db.PortfolioDaily.AsNoTracking()
                .Where(row => ids.Contains(row.AssetId) && row.Date >= opening && row.Date <= range.To)
                .ToListAsync(cancellationToken))
            .ToLookup(row => row.AssetId);

        // An asset with no row in the period has no known value: it is in neither return.
        var valued = assets.Where(asset => rows[asset.Id].Any()).ToList();
        if (valued.Count == 0)
        {
            return null;
        }

        var foreign = valued.Where(asset => asset.Currency != SnapshotBuilder.BaseCurrency).ToList();
        var fxRates = foreign.Count == 0
            ? []
            : await UsdBrlAsync(foreign.SelectMany(asset => movements[asset.Id]).Min(movement => movement.Date), range.To, cancellationToken);

        var days = valued.Select(asset => ReturnSeries.InBrl(asset.Currency, rows[asset.Id].ToList(), movements[asset.Id], fxRates));
        var twr = TimeWeightedReturn.Compute(PortfolioAggregation.Sum(days))!;

        var flows = valued.SelectMany(asset =>
        {
            var held = rows[asset.Id].OrderBy(row => row.Date).ToList();
            var open = held[0].Date == opening ? new DailyPoint(opening, held[0].ValueBrl) : new DailyPoint(DateOnly.MinValue, 0m);
            return MoneyWeightedReturn.FlowsInBrl(
                asset.Currency, movements[asset.Id], fxRates, open, new DailyPoint(range.To, held[^1].ValueBrl));
        }).ToList();

        var fx = assetId is null ? null : FxDecomposition.Split(
            valued[0].Currency, rows[valued[0].Id].ToList(), movements[valued[0].Id], fxRates);
        var benchmarks = await BenchmarksAsync(twr.Index[0].Date, range.To, cancellationToken);
        return new Measured(range, twr, MoneyWeightedReturn.Compute(flows), benchmarks, fx);
    }

    /// <summary>
    /// Each configured benchmark's index from <paramref name="baseDay"/> to <paramref name="end"/>,
    /// one point per day, ordered by code. One that cannot anchor is left out, so it is
    /// <c>null</c> in the response and absent from the chart (spec test 31).
    /// </summary>
    private async Task<List<(string Code, IReadOnlyList<DailyPoint> Index)>> BenchmarksAsync(
        DateOnly baseDay, DateOnly end, CancellationToken cancellationToken)
    {
        var configured = options.Value.Benchmarks
            .OrderBy(pair => pair.Key, StringComparer.Ordinal)
            .Select(pair => (Code: pair.Key, pair.Value, Source: string.IsNullOrWhiteSpace(pair.Value.Source) ? pair.Key : pair.Value.Source))
            .ToList();
        var sources = configured.Select(benchmark => benchmark.Source).Distinct().ToList();
        var points = (await db.Benchmarks.AsNoTracking()
                .Where(value => sources.Contains(value.Code) && value.Date > baseDay && value.Date <= end)
                .ToListAsync(cancellationToken))
            .ToLookup(value => value.Code, value => new DailyPoint(value.Date, value.Value));

        var indices = new List<(string, IReadOnlyList<DailyPoint>)>();
        foreach (var (code, benchmark, source) in configured)
        {
            var values = points[source].ToList();
            if (benchmark.Type == BenchmarkType.Level)
            {
                // A level divides by its last value on or before the base day.
                var anchor = await db.Benchmarks.AsNoTracking()
                    .Where(value => value.Code == source && value.Date <= baseDay)
                    .OrderByDescending(value => value.Date).FirstOrDefaultAsync(cancellationToken);
                if (anchor is not null)
                {
                    values.Add(new DailyPoint(anchor.Date, anchor.Value));
                }
            }

            // The spread is configured in percent a year (6), the accumulator takes a rate (0.06).
            if (BenchmarkAccumulator.Accumulate(values, benchmark.Type, baseDay, end, new Rate(benchmark.Spread / 100m)) is { } index)
            {
                indices.Add((code, index));
            }
        }

        return indices;
    }

    /// <summary>USDBRL from the latest rate on or before <paramref name="first"/> to <paramref name="last"/>: 007's FX rule.</summary>
    private async Task<IReadOnlyList<DailyPoint>> UsdBrlAsync(DateOnly first, DateOnly last, CancellationToken cancellationToken)
    {
        var rates = db.Benchmarks.AsNoTracking().Where(rate => rate.Code == Investments.SnapshotRebuild.UsdBrl && rate.Date <= last);
        var anchor = await rates.Where(rate => rate.Date <= first).MaxAsync(rate => (DateOnly?)rate.Date, cancellationToken);
        return await (anchor is { } start ? rates.Where(rate => rate.Date >= start) : rates)
            .OrderBy(rate => rate.Date).Select(rate => new DailyPoint(rate.Date, rate.Value)).ToListAsync(cancellationToken);
    }

    private static decimal Round(decimal value, int places = RatePlaces) => Math.Round(value, places, MidpointRounding.ToEven);

    private static decimal? Round(Rate? rate) => rate is { } value ? Round(value.Value) : null;

    private static decimal Round(Rate rate) => Round(rate.Value);

    private sealed record Measured(
        PeriodRange Range,
        TwrResult Twr,
        Rate? Xirr,
        IReadOnlyList<(string Code, IReadOnlyList<DailyPoint> Index)> Benchmarks,
        FxSplit? Fx);
}
