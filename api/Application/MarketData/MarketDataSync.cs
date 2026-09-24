using Finance.Api.Domain.MarketData;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Finance.Api.Application.MarketData;

/// <summary>
/// The market-data sync (006): every active asset's closes, then every benchmark series,
/// each fetched from the day after its latest stored row (or <c>BackfillYears</c> back)
/// through yesterday and upserted. One <see cref="SyncRun"/> row records the run.
/// </summary>
/// <remarks>
/// <para>
/// The upper bound is yesterday in UTC. A UTC day ends after both B3 and the US markets
/// have closed, and CoinGecko dates its points by UTC day, so every stored close is final.
/// Asking for today would store an intraday price as a close that the next run, starting
/// the day after it, would never correct.
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

    public async Task<SyncRun> RunAsync(SyncTrigger trigger, CancellationToken cancellationToken)
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

        var summary = new SortedDictionary<string, ProviderSyncSummary>(StringComparer.Ordinal);
        try
        {
            foreach (var provider in (await ItemsAsync(cancellationToken)).GroupBy(item => item.Provider))
            {
                summary[provider.Key] = await SyncProviderAsync(provider.Key, provider, cancellationToken);
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
        string provider, IEnumerable<Item> items, CancellationToken cancellationToken)
    {
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var to = today.AddDays(-1);
        var backfillFrom = today.AddYears(-options.Value.BackfillYears);

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
                var from = (await item.LatestAsync(cancellationToken))?.AddDays(1) ?? backfillFrom;
                if (from <= to)
                {
                    part.RowsWritten += await item.SyncAsync(from, to, cancellationToken);
                }

                await item.SyncedAsync(cancellationToken);
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

        var items = assets.Select(asset => new Item(
            asset.Provider.ToString(),
            asset.Ticker,
            ct => db.Set<Price>().Where(price => price.MarketAssetId == asset.Id).MaxAsync(price => (DateOnly?)price.Date, ct),
            async (from, to, ct) => await store.UpsertPricesAsync(
                asset.Id, await prices.For(asset.Provider).GetDailyClosesAsync(asset.ProviderSymbol, from, to, ct), ct),
            async ct =>
            {
                var now = clock.GetUtcNow();
                await db.Set<MarketAsset>().Where(entity => entity.Id == asset.Id)
                    .ExecuteUpdateAsync(set => set.SetProperty(entity => entity.LastSyncedAt, now), ct);
            })).ToList();

        items.AddRange(options.Value.PriceBenchmarks.Select(benchmark => new Item(
            benchmark.Value.Provider.ToString(),
            benchmark.Key,
            ct => LatestBenchmarkAsync(benchmark.Key, ct),
            async (from, to, ct) =>
            {
                var closes = await prices.For(benchmark.Value.Provider).GetDailyClosesAsync(benchmark.Value.Symbol, from, to, ct);
                return await store.UpsertBenchmarkAsync(
                    benchmark.Key, [.. closes.Select(close => new DailyValue(close.Date, close.Close))], ct);
            })));

        items.AddRange(options.Value.Bcb.Series.Keys.Order(StringComparer.Ordinal).Select(code => new Item(
            BenchmarkProviderName,
            code,
            ct => LatestBenchmarkAsync(code, ct),
            async (from, to, ct) => await store.UpsertBenchmarkAsync(
                code, await benchmarks.GetSeriesAsync(code, from, to, ct), ct))));

        return items;
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

    private static SyncRunStatus StatusOf(IEnumerable<ProviderSyncSummary> parts)
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

    /// <summary>One asset or series: its latest stored day, how to fetch and store a range, what to do once current.</summary>
    private sealed record Item(
        string Provider,
        string Label,
        Func<CancellationToken, Task<DateOnly?>> LatestAsync,
        Func<DateOnly, DateOnly, CancellationToken, Task<int>> SyncAsync,
        Func<CancellationToken, Task>? Synced = null)
    {
        public Task SyncedAsync(CancellationToken cancellationToken) =>
            Synced?.Invoke(cancellationToken) ?? Task.CompletedTask;
    }
}
