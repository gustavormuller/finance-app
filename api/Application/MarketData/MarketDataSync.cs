using Finance.Api.Domain.MarketData;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Finance.Api.Application.MarketData;

/// <summary>
/// The market-data sync: every active asset's closes, then every benchmark series, each
/// brought up to yesterday and upserted. One <see cref="SyncRun"/> row records the run.
/// </summary>
/// <remarks>
/// <para>
/// The upper bound is yesterday in UTC. A UTC day ends after both B3 and the US markets
/// have closed, and CoinGecko dates its points by UTC day, so every stored close is final.
/// Asking for today would store an intraday price as a close that the next run, starting
/// the day after it, would never correct.
/// </para>
/// <para>
/// How far back (025): an asset whose whole history has not been loaded from its current
/// provider (<see cref="MarketAsset.HistoryLoadedAt"/> null) gets it, from the provider's
/// <see cref="IPriceProvider.HistoryStart"/> or <c>BackfillYears</c> back, in place of what
/// is stored. A price benchmark with no row gets the same. A BCB series reaches back to
/// <c>MarketData:Bcb:HistoryStart</c>, once. After that, each item is asked for the days
/// after its latest stored one; a provider that revises history (Yahoo) is asked from
/// <see cref="RevisionOverlapDays"/> before it, and a stored day that now reads differently
/// reloads the whole history (<see cref="PriceRevision"/>). A stored close that changes
/// marks <see cref="MarketAsset.PricesRevisedFrom"/>, which the snapshot rebuild after the
/// sync starts from.
/// </para>
/// <para>
/// Failures are caught per item (an asset or a series), so one bad ticker never stops its
/// provider, and grouped per provider, so one provider down never stops the others. A
/// <c>429</c> stops its provider for the rest of the run. Cancellation is not a failure:
/// it ends the run as <see cref="SyncRunStatus.Failed"/> and propagates.
/// </para>
/// </remarks>
public sealed class MarketDataSync(
    AppDbContext db,
    MarketDataStore store,
    IPriceProviderRegistry prices,
    IBenchmarkProvider benchmarks,
    IOptions<MarketDataOptions> options,
    TimeProvider clock,
    ILogger<MarketDataSync> logger)
{
    /// <summary>The summary key of the <see cref="IBenchmarkProvider"/>'s series.</summary>
    public const string BenchmarkProviderName = "Bcb";

    /// <summary>How many days before its latest stored one a revising provider's series is read again (025, decision 12).</summary>
    public const int RevisionOverlapDays = 7;

    /// <summary>
    /// A BCB series stored from this close to <c>HistoryStart</c> has reached it: the first
    /// days can be a weekend or a holiday.
    /// </summary>
    private const int HistoryStartSlackDays = 7;

    /// <summary>Records a run and performs it: <see cref="StartAsync"/>, then <see cref="ExecuteAsync(Guid, CancellationToken)"/>.</summary>
    public async Task<SyncRun> RunAsync(SyncTrigger trigger, CancellationToken cancellationToken) =>
        await ExecuteAsync(await StartAsync(trigger, cancellationToken), cancellationToken);

    /// <summary>
    /// Writes the <see cref="SyncRunStatus.Running"/> row and returns it, so the manual
    /// trigger can answer with its id before the run itself happens.
    /// </summary>
    public async Task<SyncRun> StartAsync(SyncTrigger trigger, CancellationToken cancellationToken)
    {
        var run = new SyncRun
        {
            Id = Guid.NewGuid(),
            StartedAt = clock.GetUtcNow(),
            Trigger = trigger,
            Status = SyncRunStatus.Running,
        };
        db.Add(run);
        await db.SaveChangesAsync(cancellationToken);
        return run;
    }

    /// <summary>Performs a run <see cref="StartAsync"/> recorded, possibly in another scope.</summary>
    public async Task<SyncRun> ExecuteAsync(Guid syncRunId, CancellationToken cancellationToken) =>
        await ExecuteAsync(await db.Set<SyncRun>().SingleAsync(run => run.Id == syncRunId, cancellationToken), cancellationToken);

    private async Task<SyncRun> ExecuteAsync(SyncRun run, CancellationToken cancellationToken)
    {
        var summary = new SortedDictionary<string, ProviderSyncSummary>(StringComparer.Ordinal);
        try
        {
            var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
            var window = new Window(today.AddDays(-1), today.AddYears(-options.Value.BackfillYears));
            foreach (var provider in (await ItemsAsync(cancellationToken)).GroupBy(item => item.Provider))
            {
                summary[provider.Key] = await SyncProviderAsync(provider.Key, provider, window, cancellationToken);
            }
        }
        catch (Exception error)
        {
            // Cancelled at shutdown, or the database failed outside any one item: the row
            // must not stay Running forever.
            logger.LogError(error, "Market-data sync {SyncRunId} stopped before it finished.", run.Id);
            try
            {
                await FinishAsync(run, summary, SyncRunStatus.Failed, CancellationToken.None);
            }
            catch (Exception finishing)
            {
                logger.LogError(finishing, "Market-data sync {SyncRunId} could not be marked Failed.", run.Id);
            }

            throw;
        }

        await FinishAsync(run, summary, StatusOf(summary.Values), cancellationToken);
        logger.LogInformation("Market-data sync {SyncRunId} finished: {Status}.", run.Id, run.Status);
        return run;
    }

    private async Task<ProviderSyncSummary> SyncProviderAsync(
        string provider, IEnumerable<Item> items, Window window, CancellationToken cancellationToken)
    {
        var part = new ProviderSyncSummary();
        string? rateLimited = null;
        foreach (var item in items)
        {
            if (rateLimited is not null)
            {
                Fail(part, item.Label, rateLimited);
                continue;
            }

            try
            {
                part.RowsWritten += await item.SyncAsync(window, cancellationToken);
                part.ItemsSynced++;
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                logger.LogWarning(error, "Market-data sync: {Provider} {Item} failed.", provider, item.Label);
                Fail(part, item.Label, SyncErrorText.For(error));
                if (error is ProviderRateLimitedException)
                {
                    rateLimited = SyncErrorText.For(error);
                }
            }
        }

        return part;
    }

    /// <summary>Active assets by provider and ticker, then the price benchmarks, then the BCB series.</summary>
    private async Task<List<Item>> ItemsAsync(CancellationToken cancellationToken)
    {
        var assets = await db.Set<MarketAsset>().AsNoTracking()
            .Where(asset => asset.IsActive)
            .OrderBy(asset => asset.Provider).ThenBy(asset => asset.Ticker)
            .ToListAsync(cancellationToken);

        var items = assets
            .Select(asset => new Item(asset.Provider.ToString(), asset.Ticker, (window, ct) => SyncAssetAsync(asset, window, ct)))
            .ToList();

        items.AddRange(options.Value.PriceBenchmarks.Select(benchmark => new Item(
            benchmark.Value.Provider.ToString(),
            benchmark.Key,
            (window, ct) => SyncPriceBenchmarkAsync(benchmark.Key, benchmark.Value, window, ct))));

        items.AddRange(options.Value.Bcb.Series.Keys.Order(StringComparer.Ordinal).Select(code => new Item(
            BenchmarkProviderName, code, (window, ct) => SyncSeriesAsync(code, window, ct))));

        return items;
    }

    private async Task<int> SyncAssetAsync(MarketAsset asset, Window window, CancellationToken cancellationToken)
    {
        var provider = prices.For(asset.Provider);
        var latest = await db.Set<Price>().Where(price => price.MarketAssetId == asset.Id)
            .MaxAsync(price => (DateOnly?)price.Date, cancellationToken);

        var rows = asset.HistoryLoadedAt is null ? await LoadHistoryAsync(asset, provider, window, replace: true, cancellationToken)
            : latest is null ? await LoadHistoryAsync(asset, provider, window, replace: false, cancellationToken)
            : provider.RevisesHistory ? await RefreshAsync(asset, provider, latest.Value, window, cancellationToken)
            : latest.Value < window.To
                ? await store.UpsertPricesAsync(
                    asset.Id, await provider.GetDailyClosesAsync(asset.ProviderSymbol, latest.Value.AddDays(1), window.To, cancellationToken), cancellationToken)
                : 0;

        var now = clock.GetUtcNow();
        await db.Set<MarketAsset>().Where(entity => entity.Id == asset.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(entity => entity.LastSyncedAt, now), cancellationToken);
        return rows;
    }

    /// <summary>
    /// The asset's whole history from its provider, in one transaction with its marks. When
    /// <paramref name="replace"/>, it takes the place of what is stored: a series from another
    /// source is never merged with this one. A provider that returns nothing leaves both the
    /// stored prices and the pending load as they are.
    /// </summary>
    private async Task<int> LoadHistoryAsync(
        MarketAsset asset, IPriceProvider provider, Window window, bool replace, CancellationToken cancellationToken)
    {
        var closes = await provider.GetDailyClosesAsync(
            asset.ProviderSymbol, provider.HistoryStart ?? window.BackfillFrom, window.To, cancellationToken);
        if (closes.Count == 0)
        {
            return 0;
        }

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var stored = await db.Set<Price>().AsNoTracking().Where(price => price.MarketAssetId == asset.Id)
            .ToDictionaryAsync(price => price.Date, price => price.Close, cancellationToken);
        var rows = replace
            ? await store.ReplacePricesAsync(asset.Id, closes, cancellationToken)
            : await store.UpsertPricesAsync(asset.Id, closes, cancellationToken);
        await MarkRevisedAsync(asset.Id, PriceRevision.EarliestChange(stored, closes, replace), cancellationToken);

        // Only if the source is still the one read: an edit during the run keeps its pending load.
        var now = clock.GetUtcNow();
        await db.Set<MarketAsset>()
            .Where(entity => entity.Id == asset.Id && entity.Provider == asset.Provider && entity.ProviderSymbol == asset.ProviderSymbol)
            .ExecuteUpdateAsync(set => set.SetProperty(entity => entity.HistoryLoadedAt, now), cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return rows;
    }

    /// <summary>
    /// A revising provider's days since <see cref="RevisionOverlapDays"/> before the latest
    /// stored one. A stored day that reads differently reloads the whole history; otherwise
    /// only the days not stored yet are written.
    /// </summary>
    private async Task<int> RefreshAsync(
        MarketAsset asset, IPriceProvider provider, DateOnly latest, Window window, CancellationToken cancellationToken)
    {
        var from = latest.AddDays(-RevisionOverlapDays);
        var closes = await provider.GetDailyClosesAsync(asset.ProviderSymbol, from, window.To, cancellationToken);
        var stored = await db.Set<Price>().AsNoTracking().Where(price => price.MarketAssetId == asset.Id && price.Date >= from)
            .ToDictionaryAsync(price => price.Date, price => new StoredPrice(price.Close, price.AdjustedClose), cancellationToken);

        if (PriceRevision.Revised(stored, closes))
        {
            logger.LogInformation("Market-data sync: {Provider} revised {Ticker}; reloading its whole history.", asset.Provider, asset.Ticker);
            return await LoadHistoryAsync(asset, provider, window, replace: false, cancellationToken);
        }

        List<DailyClose> fresh = [.. closes.Where(close => !stored.ContainsKey(close.Date))];
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        var rows = await store.UpsertPricesAsync(asset.Id, fresh, cancellationToken);

        // A day filled in before the latest stored one replaces the close the snapshots carried over it.
        await MarkRevisedAsync(
            asset.Id, PriceRevision.EarliestChange(stored.ToDictionary(price => price.Key, price => price.Value.Close), fresh, replacing: false),
            cancellationToken);
        await transaction.CommitAsync(cancellationToken);
        return rows;
    }

    /// <summary>Moves <see cref="MarketAsset.PricesRevisedFrom"/> back to <paramref name="day"/>, never forward.</summary>
    private async Task MarkRevisedAsync(Guid marketAssetId, DateOnly? day, CancellationToken cancellationToken)
    {
        if (day is not { } revised)
        {
            return;
        }

        await db.Set<MarketAsset>()
            .Where(entity => entity.Id == marketAssetId && (entity.PricesRevisedFrom == null || entity.PricesRevisedFrom > revised))
            .ExecuteUpdateAsync(set => set.SetProperty(entity => entity.PricesRevisedFrom, revised), cancellationToken);
    }

    /// <summary>
    /// A benchmark read as an asset's closes (IVVB11): its total-return level,
    /// <see cref="DailyClose.AdjustedClose"/> where the provider sends one (025, decision 14).
    /// Loaded whole when it has no row; refreshed like an asset after that.
    /// </summary>
    private async Task<int> SyncPriceBenchmarkAsync(string code, PriceBenchmark benchmark, Window window, CancellationToken cancellationToken)
    {
        var provider = prices.For(benchmark.Provider);
        var latest = await LatestBenchmarkAsync(code, cancellationToken);

        Task<IReadOnlyList<DailyClose>> Read(DateOnly from) => provider.GetDailyClosesAsync(benchmark.Symbol, from, window.To, cancellationToken);
        Task<int> Write(IEnumerable<DailyClose> closes) =>
            store.UpsertBenchmarkAsync(code, [.. closes.Select(close => new DailyValue(close.Date, close.AdjustedClose ?? close.Close))], cancellationToken);

        if (latest is null)
        {
            return await Write(await Read(provider.HistoryStart ?? window.BackfillFrom));
        }

        if (!provider.RevisesHistory)
        {
            return latest.Value < window.To ? await Write(await Read(latest.Value.AddDays(1))) : 0;
        }

        var from = latest.Value.AddDays(-RevisionOverlapDays);
        var closes = await Read(from);
        var stored = await db.Set<Benchmark>().AsNoTracking().Where(value => value.Code == code && value.Date >= from)
            .ToDictionaryAsync(value => value.Date, value => value.Value, cancellationToken);

        return closes.Any(close => stored.TryGetValue(close.Date, out var value) && PriceRevision.Differs(value, close.AdjustedClose ?? close.Close))
            ? await Write(await Read(provider.HistoryStart ?? window.BackfillFrom))
            : await Write(closes.Where(close => !stored.ContainsKey(close.Date)));
    }

    /// <summary>A BCB series: the days after its latest stored one, and once, the years back to <c>HistoryStart</c>.</summary>
    private async Task<int> SyncSeriesAsync(string code, Window window, CancellationToken cancellationToken)
    {
        var historyStart = options.Value.Bcb.HistoryStart;
        var values = db.Set<Benchmark>().Where(value => value.Code == code);
        var earliest = await values.MinAsync(value => (DateOnly?)value.Date, cancellationToken);
        var latest = await values.MaxAsync(value => (DateOnly?)value.Date, cancellationToken);

        async Task<int> SyncAsync(DateOnly from, DateOnly to) =>
            from > to ? 0 : await store.UpsertBenchmarkAsync(code, await benchmarks.GetSeriesAsync(code, from, to, cancellationToken), cancellationToken);

        if (earliest is null || latest is null)
        {
            return await SyncAsync(historyStart ?? window.BackfillFrom, window.To);
        }

        var older = historyStart is { } start && earliest.Value > start.AddDays(HistoryStartSlackDays)
            ? await SyncAsync(start, earliest.Value.AddDays(-1))
            : 0;
        return older + await SyncAsync(latest.Value.AddDays(1), window.To);
    }

    private Task<DateOnly?> LatestBenchmarkAsync(string code, CancellationToken cancellationToken) =>
        db.Set<Benchmark>().Where(value => value.Code == code).MaxAsync(value => (DateOnly?)value.Date, cancellationToken);

    private async Task FinishAsync(
        SyncRun run, IReadOnlyDictionary<string, ProviderSyncSummary> summary, SyncRunStatus status, CancellationToken cancellationToken)
    {
        run.FinishedAt = clock.GetUtcNow();
        run.Status = status;
        run.Summary = SyncSummaryJson.Write(summary);
        await db.SaveChangesAsync(cancellationToken);
    }

    /// <summary>Succeeded with no failure, Failed with no success, PartialFailure otherwise.</summary>
    internal static SyncRunStatus StatusOf(IEnumerable<ProviderSyncSummary> parts)
    {
        var (synced, failed) = parts.Aggregate((0, 0), (sum, part) => (sum.Item1 + part.ItemsSynced, sum.Item2 + part.ItemsFailed));
        return failed == 0 ? SyncRunStatus.Succeeded
            : synced == 0 ? SyncRunStatus.Failed
            : SyncRunStatus.PartialFailure;
    }

    private static void Fail(ProviderSyncSummary part, string item, string error)
    {
        part.ItemsFailed++;
        part.Error ??= error;
        part.Failures.Add(new SyncFailure(item, error));
    }

    /// <summary>The run's bounds: yesterday, and where a first load reaches back to without a history start.</summary>
    private readonly record struct Window(DateOnly To, DateOnly BackfillFrom);

    /// <summary>One asset or series, and how to bring it up to date; returns the rows written.</summary>
    private sealed record Item(string Provider, string Label, Func<Window, CancellationToken, Task<int>> SyncAsync);
}
