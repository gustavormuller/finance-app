using Finance.Api.Application.MarketData;
using Finance.Api.Application.Returns;
using Finance.Api.Domain.Compare;
using Finance.Api.Domain.MarketData;
using Finance.Api.Domain.Returns;
using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Finance.Api.Application.Compare;

/// <summary>
/// 026's read: catalogue closes and stored benchmark series rebased to 100 on a common start,
/// in their own currency or at PTAX. It reads shared market data only, so nothing here goes
/// through a user filter, and only the local database (ARCHITECTURE.md, principle 6).
/// </summary>
public sealed class CompareQueries(AppDbContext db, TimeProvider clock, IOptions<MarketDataOptions> options)
{
    private const int RatePlaces = 10;
    private const int IndexPlaces = 6;
    private const string Brl = "BRL";
    private const string Usd = "USD";

    /// <summary>
    /// Every stored benchmark is Brazilian: CDI, SELIC and IPCA are rates on reais, USDBRL is
    /// reais per dollar, IVVB11 trades in reais.
    /// </summary>
    private const string BenchmarkCurrency = Brl;

    /// <summary>PTAX, reais per dollar: the rate a series converts at (decision 10).</summary>
    private static readonly string[] Ptax = [Benchmark.UsdBrl];

    private DateOnly Today => DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);

    public async Task<CompareResult> CompareAsync(CompareRequest request, CancellationToken cancellationToken)
    {
        var target = request.Currency switch { CompareCurrency.Brl => Brl, CompareCurrency.Usd => Usd, _ => null };
        var (described, violations) = await DescribeAsync(request.Series, target, cancellationToken);
        if (violations.Count > 0)
        {
            return new CompareResult(null, violations);
        }

        var converted = described.Select(series => target is not null && series.Currency != target).ToList();
        var converting = converted.Contains(true);
        var ptax = converting ? Coverage(await StoredDaysAsync(Ptax, Ptax, cancellationToken), Benchmark.UsdBrl, BenchmarkUnit.Level) : default;
        var coverage = described
            .Select((series, position) => converted[position] ? Within(series.Coverage, ptax) : series.Coverage)
            .ToList();

        var window = ComparePeriods.Window(request.Period, request.From, request.To, Today);
        var plan = ComparePeriods.Plan(window, coverage);
        Comparison? comparison = null;
        if (plan.Outcome == CompareOutcome.Compared)
        {
            var rates = converting ? await LevelAsync(Values(Benchmark.UsdBrl), plan.Start, plan.End, cancellationToken) : [];
            var drawn = new List<IReadOnlyList<DailyPoint>>();
            for (var position = 0; position < described.Count; position++)
            {
                if (!plan.HasData[position])
                {
                    continue;
                }

                var points = await ObservationsAsync(described[position], plan.Start, plan.End, cancellationToken);
                drawn.Add(!converted[position] ? points
                    : target == Brl ? PtaxConversion.UsdToBrl(points, rates)
                    : PtaxConversion.BrlToUsd(points, rates));
            }

            comparison = SeriesComparison.Compare(drawn, plan.Start, plan.End);
        }

        return new CompareResult(View(request.Currency, described, coverage, window, plan, comparison), []);
    }

    /// <summary>Each series' name, currency and own data, in the order asked; or why one cannot be compared.</summary>
    private async Task<(List<Described> Series, List<RuleViolation> Violations)> DescribeAsync(
        IReadOnlyList<SeriesKey> keys, string? target, CancellationToken cancellationToken)
    {
        var ids = keys.Select(key => key.AssetId).OfType<Guid>().ToList();
        var assets = await db.MarketAssets.AsNoTracking()
            .Where(asset => ids.Contains(asset.Id))
            .Select(asset => new
            {
                asset.Id,
                asset.Ticker,
                asset.Name,
                asset.Class,
                asset.Currency,
                // Each end is one probe of the primary key.
                First = db.Prices.Where(price => price.MarketAssetId == asset.Id && price.Close > 0m)
                    .OrderBy(price => price.Date).Select(price => (DateOnly?)price.Date).FirstOrDefault(),
                Last = db.Prices.Where(price => price.MarketAssetId == asset.Id && price.Close > 0m)
                    .OrderByDescending(price => price.Date).Select(price => (DateOnly?)price.Date).FirstOrDefault(),
            })
            .ToDictionaryAsync(asset => asset.Id, cancellationToken);

        var stored = StoredSeries(options.Value);
        var codes = keys.Select(key => key.Code).OfType<string>().Where(stored.ContainsKey).ToArray();
        var days = await StoredDaysAsync(codes, [.. codes.Where(code => stored[code] == BenchmarkUnit.Level)], cancellationToken);

        var described = new List<Described>();
        var violations = new List<RuleViolation>();
        foreach (var key in keys)
        {
            if (key.AssetId is { } id)
            {
                if (assets.TryGetValue(id, out var asset))
                {
                    described.Add(new Described(
                        key, asset.Ticker, asset.Name, asset.Class, asset.Currency, null, new SeriesCoverage(asset.First, asset.Last)));
                }
                else
                {
                    violations.Add(new RuleViolation("series", $"Ativo não encontrado no catálogo: {id}."));
                }
            }
            else if (stored.TryGetValue(key.Code!, out var unit))
            {
                described.Add(new Described(key, null, null, null, BenchmarkCurrency, unit, Coverage(days, key.Code!, unit)));
            }
            else
            {
                violations.Add(new RuleViolation("series", $"Referência desconhecida: {key.Code}."));
            }
        }

        if (target is not null)
        {
            violations.AddRange(described
                .Where(series => series.Currency is not (Brl or Usd))
                .Select(series => new RuleViolation(
                    "currency",
                    $"Não é possível converter {series.Ticker ?? series.Key.Code} de {series.Currency} para {target}: "
                    + "só há cotação entre reais e dólares.")));
        }

        return (described, violations);
    }

    /// <summary>
    /// A series' points from just before the start through the end: an asset's closes and a
    /// level's values from their last one on or before the start, a rate as its index.
    /// </summary>
    private async Task<IReadOnlyList<DailyPoint>> ObservationsAsync(
        Described series, DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        if (series.Key.AssetId is { } id)
        {
            var prices = db.Prices.AsNoTracking().Where(price => price.MarketAssetId == id && price.Close > 0m);
            var anchor = await prices.Where(price => price.Date <= start).OrderByDescending(price => price.Date)
                .FirstAsync(cancellationToken);
            var after = await prices.Where(price => price.Date > start && price.Date <= end).OrderBy(price => price.Date)
                .ToListAsync(cancellationToken);
            return [.. after.Prepend(anchor).Select(price => new DailyPoint(price.Date, ComparisonPrice.ValueOf(price)))];
        }

        if (series.Unit == BenchmarkUnit.Level)
        {
            return await LevelAsync(Values(series.Key.Code!), start, end, cancellationToken);
        }

        var rows = await Values(series.Key.Code!).Where(value => value.Date > start && value.Date <= end)
            .Select(value => new DailyPoint(value.Date, value.Value)).ToListAsync(cancellationToken);
        return RateIndex.Observations(rows, ReturnsOptions.TypeFor(series.Unit!.Value), start, end);
    }

    private IQueryable<Benchmark> Values(string code) => db.Benchmarks.AsNoTracking().Where(value => value.Code == code);

    /// <summary>A level from its last positive value on or before the start through the end.</summary>
    private static async Task<List<DailyPoint>> LevelAsync(
        IQueryable<Benchmark> values, DateOnly start, DateOnly end, CancellationToken cancellationToken)
    {
        var positive = values.Where(value => value.Value > 0m);
        var anchor = await positive.Where(value => value.Date <= start).OrderByDescending(value => value.Date)
            .Select(value => new DailyPoint(value.Date, value.Value)).FirstAsync(cancellationToken);
        var after = await positive.Where(value => value.Date > start && value.Date <= end).OrderBy(value => value.Date)
            .Select(value => new DailyPoint(value.Date, value.Value)).ToListAsync(cancellationToken);
        return [anchor, .. after];
    }

    /// <summary>The first and last stored day of each code; a level's only where it is positive.</summary>
    private async Task<Dictionary<string, (DateOnly First, DateOnly Last)>> StoredDaysAsync(
        string[] codes, string[] levels, CancellationToken cancellationToken) =>
        (await db.Benchmarks.AsNoTracking()
            .Where(value => codes.Contains(value.Code) && (!levels.Contains(value.Code) || value.Value > 0m))
            .GroupBy(value => value.Code)
            .Select(group => new { Code = group.Key, First = group.Min(value => value.Date), Last = group.Max(value => value.Date) })
            .ToListAsync(cancellationToken))
        .ToDictionary(row => row.Code, row => (row.First, row.Last));

    /// <summary>A rate's first row compounds on its own date, so its index can start the day before.</summary>
    private static SeriesCoverage Coverage(Dictionary<string, (DateOnly First, DateOnly Last)> days, string code, BenchmarkUnit unit) =>
        days.TryGetValue(code, out var stored)
            ? new SeriesCoverage(unit == BenchmarkUnit.Level ? stored.First : stored.First.AddDays(-1), stored.Last)
            : default;

    /// <summary>A converted series begins once both it and PTAX have; its last observation stays its own.</summary>
    private static SeriesCoverage Within(SeriesCoverage own, SeriesCoverage ptax) =>
        own.First is { } first && ptax.First is { } rate ? new SeriesCoverage(first > rate ? first : rate, own.Last) : own with { First = null };

    /// <summary>Every series 006 stores as a benchmark, with the unit its values are in.</summary>
    private static Dictionary<string, BenchmarkUnit> StoredSeries(MarketDataOptions marketData)
    {
        var stored = new Dictionary<string, BenchmarkUnit>(StringComparer.Ordinal);
        foreach (var (code, series) in marketData.Bcb.Series)
        {
            stored[code.ToUpperInvariant()] = series.Unit;
        }

        foreach (var (code, benchmark) in marketData.PriceBenchmarks)
        {
            stored[code.ToUpperInvariant()] = benchmark.Unit;
        }

        return stored;
    }

    private static CompareView View(
        CompareCurrency currency,
        List<Described> described,
        List<SeriesCoverage> coverage,
        CompareWindow window,
        ComparePlan plan,
        Comparison? comparison)
    {
        // The comparison holds only the drawn series: where each requested one sits among them.
        var drawn = 0;
        var positions = plan.HasData.Select(hasData => comparison is not null && hasData ? drawn++ : (int?)null).ToList();
        var series = described.Select((one, position) =>
        {
            var change = positions[position] is { } index ? comparison!.Changes[index] : (SeriesChange?)null;
            return new CompareSeriesView(
                one.Key.ToString(),
                one.Key.AssetId is null ? "benchmark" : "asset",
                one.Ticker,
                one.Name,
                one.Class,
                one.Key.Code,
                one.Currency,
                coverage[position].First,
                coverage[position].Last,
                plan.HasData[position],
                change is { } value ? Round(value.Change.Value, RatePlaces) : null,
                change?.Annualised is { } annualised ? Round(annualised.Value, RatePlaces) : null);
        }).ToList();

        var text = currency switch { CompareCurrency.Brl => Brl, CompareCurrency.Usd => Usd, _ => "original" };
        if (comparison is null)
        {
            return new CompareView(text, null, series, []);
        }

        var points = ComparisonSampling.Keep(comparison.Dates)
            .Select(date => new ComparePointView(
                comparison.Dates[date],
                [.. positions.Select(index => index is { } drawnAt ? Round(comparison.Indices[drawnAt][date], IndexPlaces) : (decimal?)null)]))
            .ToList();
        var period = new ComparePeriodView(
            plan.Start, plan.End, plan.End.DayNumber - plan.Start.DayNumber, window.From is not { } from || plan.Start > from);
        return new CompareView(text, period, series, points);
    }

    private static decimal Round(decimal value, int places) => Math.Round(value, places, MidpointRounding.ToEven);

    /// <summary>A series as found: its key, what the catalogue says of an asset, its currency, a benchmark's unit, its own data.</summary>
    private sealed record Described(
        SeriesKey Key,
        string? Ticker,
        string? Name,
        MarketAssetClass? Class,
        string Currency,
        BenchmarkUnit? Unit,
        SeriesCoverage Coverage);
}
