using Finance.Api.Application.MarketData;
using Finance.Api.Domain.MarketData;
using Finance.Api.Infrastructure;
using Finance.Api.Tests.Unit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 006 checkpoint 3: <see cref="MarketDataSync"/>, spec tests 10 to 14, against fake
/// providers and a real PostgreSQL. The spec files these under Unit, but the sync reads
/// and writes through <see cref="AppDbContext"/> (ADR-016) and the upsert is PostgreSQL
/// SQL, so they need the database.
/// </summary>
/// <remarks>
/// The sync reads every active asset in the catalogue, which is shared, so each test runs
/// against a database of its own: rows from other tests would change what is synced.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed partial class MarketDataSyncTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Yesterday = new(2026, 9, 23);

    private readonly FakePriceProvider brapi = new(ProviderKind.Brapi);

    private readonly FakePriceProvider coinGecko = new(ProviderKind.CoinGecko);

    private readonly FakePriceProvider twelveData = new(ProviderKind.TwelveData);

    private readonly FakeBenchmarkProvider bcb = new();

    private readonly MarketDataOptions options = OnlyCdi(MarketDataProviderHarness.Options());

    private string connectionString = "";

    /// <summary>Spec test 11, and the upper bound: yesterday, so an intraday price is never stored as a close.</summary>
    [Fact]
    public async Task A_first_sync_backfills_BackfillYears_up_to_yesterday()
    {
        var ct = TestContext.Current.CancellationToken;
        var asset = await AssetAsync(ProviderKind.Brapi, "PETR4", ct);

        var run = await SyncAsync(ct);

        var backfillFrom = new DateOnly(2021, 9, 24);
        Assert.Equal([("PETR4", backfillFrom, Yesterday)], brapi.Calls);
        Assert.Equal([("CDI", backfillFrom, Yesterday)], bcb.Calls);
        Assert.Equal(SyncRunStatus.Succeeded, run.Status);
        Assert.Equal(SyncTrigger.Scheduled, run.Trigger);
        Assert.Equal(Now, run.StartedAt);
        Assert.Equal(Now, run.FinishedAt);

        await using var db = Context();
        Assert.Equal(Now, (await db.Set<MarketAsset>().SingleAsync(entity => entity.Id == asset.Id, ct)).LastSyncedAt);
        Assert.Equal([Yesterday], await db.Set<Price>().Select(price => price.Date).ToListAsync(ct));
        Assert.Equal(1, await db.Set<Benchmark>().CountAsync(value => value.Code == "CDI" && value.Date == Yesterday, ct));

        var summary = SyncSummaryJson.Read(run.Summary);
        Assert.Equal(1, summary["Brapi"].RowsWritten);
        Assert.Equal(1, summary["Bcb"].RowsWritten);
        Assert.Null(summary["Brapi"].Error);
    }

    /// <summary>Spec test 10, for an asset and for a benchmark series.</summary>
    [Fact]
    public async Task The_next_sync_starts_the_day_after_the_latest_stored_value()
    {
        var ct = TestContext.Current.CancellationToken;
        var asset = await AssetAsync(ProviderKind.CoinGecko, "bitcoin", ct);
        await using (var db = Context())
        {
            var store = new MarketDataStore(db);
            await store.UpsertPricesAsync(asset.Id, [new(new(2026, 9, 10), 1m), new(new(2026, 9, 1), 1m)], ct);
            await store.UpsertBenchmarkAsync("CDI", [new(new(2026, 9, 15), 0.05m)], ct);
        }

        await SyncAsync(ct);

        Assert.Equal([("bitcoin", new DateOnly(2026, 9, 11), Yesterday)], coinGecko.Calls);
        Assert.Equal([("CDI", new DateOnly(2026, 9, 16), Yesterday)], bcb.Calls);
    }

    [Fact]
    public async Task A_series_already_stored_through_yesterday_is_not_requested_and_counts_as_synced()
    {
        var ct = TestContext.Current.CancellationToken;
        var asset = await AssetAsync(ProviderKind.TwelveData, "AAPL", ct);
        await using (var db = Context())
        {
            await new MarketDataStore(db).UpsertPricesAsync(asset.Id, [new(Yesterday, 1m)], ct);
        }

        var run = await SyncAsync(ct);

        Assert.Empty(twelveData.Calls);
        Assert.Equal(SyncRunStatus.Succeeded, run.Status);
        Assert.Equal(1, SyncSummaryJson.Read(run.Summary)["TwelveData"].ItemsSynced);
    }

    [Fact]
    public async Task Inactive_assets_are_skipped()
    {
        var ct = TestContext.Current.CancellationToken;
        await AssetAsync(ProviderKind.Brapi, "OIBR3", ct, active: false);

        await SyncAsync(ct);

        Assert.Empty(brapi.Calls);
    }

    [Fact]
    public async Task The_run_is_recorded_as_running_while_it_works()
    {
        var ct = TestContext.Current.CancellationToken;
        await AssetAsync(ProviderKind.Brapi, "PETR4", ct);
        SyncRun? seen = null;
        brapi.Respond = (_, _, _) =>
        {
            using var db = Context();
            seen = db.Set<SyncRun>().AsNoTracking().Single();
            return [];
        };

        var run = await SyncAsync(ct, SyncTrigger.Manual);

        Assert.NotNull(seen);
        Assert.Equal(run.Id, seen.Id);
        Assert.Equal(SyncRunStatus.Running, seen.Status);
        Assert.Null(seen.FinishedAt);
        Assert.Equal(SyncTrigger.Manual, seen.Trigger);
    }

    /// <summary>IVVB11 is not a BCB series: brapi serves it as closes, stored as a benchmark (spec, Configuration).</summary>
    [Fact]
    public async Task A_price_benchmark_is_read_from_its_price_provider_and_stored_as_a_benchmark()
    {
        var ct = TestContext.Current.CancellationToken;
        options.PriceBenchmarks["IVVB11"] = new PriceBenchmark
        {
            Provider = ProviderKind.Brapi, Symbol = "IVVB11", Unit = BenchmarkUnit.Level,
        };
        brapi.Respond = (_, _, to) => [new(to.AddDays(-1), 390.5m), new(to, 391.25m)];

        var run = await SyncAsync(ct);

        Assert.Equal([("IVVB11", new DateOnly(2021, 9, 24), Yesterday)], brapi.Calls);
        await using var db = Context();
        Assert.Equal(
            [390.5m, 391.25m],
            await db.Set<Benchmark>().Where(value => value.Code == "IVVB11").OrderBy(value => value.Date)
                .Select(value => value.Value).ToListAsync(ct));
        Assert.Equal(2, SyncSummaryJson.Read(run.Summary)["Brapi"].RowsWritten);
    }

    private async Task<SyncRun> SyncAsync(CancellationToken ct, SyncTrigger trigger = SyncTrigger.Scheduled)
    {
        await EnsureDatabaseAsync(ct);
        await using var db = Context();
        var sync = new MarketDataSync(
            db,
            new MarketDataStore(db),
            new PriceProviderRegistry([brapi, coinGecko, twelveData]),
            bcb,
            Microsoft.Extensions.Options.Options.Create(options),
            new FixedClock(Now),
            NullLogger<MarketDataSync>.Instance);

        return await sync.RunAsync(trigger, ct);
    }

    private async Task<MarketAsset> AssetAsync(ProviderKind provider, string symbol, CancellationToken ct, bool active = true)
    {
        await EnsureDatabaseAsync(ct);
        var asset = new MarketAsset
        {
            Id = Guid.NewGuid(),
            Ticker = symbol.ToUpperInvariant(),
            Name = symbol,
            Class = MarketAssetClass.StockBr,
            Currency = "BRL",
            Provider = provider,
            ProviderSymbol = symbol,
            IsActive = active,
            CreatedAt = Now.AddDays(-30),
        };
        await using var db = Context();
        db.Set<MarketAsset>().Add(asset);
        await db.SaveChangesAsync(ct);
        return asset;
    }

    private async Task EnsureDatabaseAsync(CancellationToken ct)
    {
        if (connectionString != "")
        {
            return;
        }

        connectionString = await postgres.CreateEmptyDatabaseAsync(ct);
        await using var db = Context();
        await db.Database.MigrateAsync(ct);
    }

    private AppDbContext Context() => TransactionsFixtures.ContextFor(connectionString, null);

    private static MarketDataOptions OnlyCdi(MarketDataOptions options)
    {
        options.Bcb.Series.Remove("IPCA");
        return options;
    }
}
