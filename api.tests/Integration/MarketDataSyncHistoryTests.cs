using Finance.Api.Application.MarketData;
using Finance.Api.Domain.MarketData;
using Finance.Api.Infrastructure;
using Finance.Api.Tests.Unit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// Spec 025 tests 16 to 19, 21 and 22: whole histories loaded once, prices replaced when an
/// asset's source changes, Yahoo's revisions caught by re-reading the last week, and BCB
/// reaching back to 1994. Against fake providers and a real PostgreSQL, a database per test.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class MarketDataSyncHistoryTests(PostgresFixture postgres)
{
    private static readonly DateTimeOffset Now = new(2026, 9, 24, 12, 0, 0, TimeSpan.Zero);

    private static readonly DateOnly Yesterday = new(2026, 9, 23);

    private static readonly DateOnly WholeHistory = new(1900, 1, 1);

    private readonly FakePriceProvider yahoo = new(ProviderKind.Yahoo) { HistoryStart = WholeHistory, RevisesHistory = true };

    private readonly FakeBenchmarkProvider bcb = new();

    private readonly MarketDataOptions options = NoBcb(MarketDataProviderHarness.Options());

    private string connectionString = "";

    /// <summary>Spec test 16.</summary>
    [Fact]
    public async Task A_new_yahoo_asset_loads_its_whole_history_with_adjusted_closes_and_is_stamped()
    {
        var ct = TestContext.Current.CancellationToken;
        var asset = await YahooAssetAsync("ITUB4.SA", loaded: false, ct);
        yahoo.Respond = (_, _, _) => [new(new(2000, 12, 21), 178.80m, 1.12978303m), new(Yesterday, 44.28m, 44.26181793m)];

        var run = await SyncAsync(ct);

        Assert.Equal([("ITUB4.SA", WholeHistory, Yesterday)], yahoo.Calls);
        await using var db = Context();
        Assert.Equal(
            [(new DateOnly(2000, 12, 21), 178.80m, (decimal?)1.12978303m), (Yesterday, 44.28m, 44.26181793m)],
            (await PricesAsync(db, asset, ct)).Select(price => (price.Date, price.Close, price.AdjustedClose)));
        var stored = await db.Set<MarketAsset>().AsNoTracking().SingleAsync(entity => entity.Id == asset.Id, ct);
        Assert.Equal((Now, Now, (DateOnly?)null), (stored.HistoryLoadedAt, stored.LastSyncedAt, stored.PricesRevisedFrom));
        Assert.Equal(2, SyncSummaryJson.Read(run.Summary)["Yahoo"].RowsWritten);
    }

    /// <summary>Spec test 17: an edited asset's old prices go; the snapshots are rebuilt from the earliest day either source had.</summary>
    [Fact]
    public async Task An_asset_whose_source_changed_has_its_prices_replaced_not_merged()
    {
        var ct = TestContext.Current.CancellationToken;
        var asset = await YahooAssetAsync("PETR4.SA", loaded: false, ct);
        await StoreAsync(asset, ct, [.. Days(new(2026, 9, 1), Yesterday).Select(day => new DailyClose(day, 30m))]);
        yahoo.Respond = (_, _, _) => [.. Days(new(2026, 9, 10), Yesterday).Select(day => new DailyClose(day, 31m, 30.5m))];

        await SyncAsync(ct);

        await using var db = Context();
        var prices = await PricesAsync(db, asset, ct);
        Assert.Equal(Days(new(2026, 9, 10), Yesterday), prices.Select(price => price.Date));
        Assert.All(prices, price => Assert.Equal((31m, (decimal?)30.5m), (price.Close, price.AdjustedClose)));
        var stored = await db.Set<MarketAsset>().AsNoTracking().SingleAsync(entity => entity.Id == asset.Id, ct);
        Assert.Equal((Now, (DateOnly?)new DateOnly(2026, 9, 1)), (stored.HistoryLoadedAt, stored.PricesRevisedFrom));
    }

    /// <summary>Spec test 18: the last week is read again, and only the days not stored are written.</summary>
    [Fact]
    public async Task An_overlap_that_matches_writes_only_the_new_days()
    {
        var ct = TestContext.Current.CancellationToken;
        var asset = await YahooAssetAsync("WEGE3.SA", loaded: true, ct);
        await StoreAsync(asset, ct, [.. Days(new(2026, 9, 1), new(2026, 9, 21)).Select(day => new DailyClose(day, 50m, 49m))]);
        yahoo.Respond = (_, from, to) => [.. Days(from, to).Select(day => new DailyClose(day, 50m, 49m))];

        var run = await SyncAsync(ct);

        Assert.Equal([("WEGE3.SA", new DateOnly(2026, 9, 14), Yesterday)], yahoo.Calls);
        Assert.Equal(2, SyncSummaryJson.Read(run.Summary)["Yahoo"].RowsWritten);
        await using var db = Context();
        Assert.Equal(Days(new(2026, 9, 1), Yesterday), (await PricesAsync(db, asset, ct)).Select(price => price.Date));
        Assert.Null((await db.Set<MarketAsset>().AsNoTracking().SingleAsync(entity => entity.Id == asset.Id, ct)).PricesRevisedFrom);
    }

    /// <summary>
    /// Spec test 19: a dividend rebased the adjusted closes, so the whole history is read again
    /// and every row rewritten; a day Yahoo no longer sends stays; the closes did not move, so
    /// no snapshot needs rebuilding.
    /// </summary>
    [Fact]
    public async Task An_overlap_whose_adjusted_close_moved_reloads_and_upserts_the_whole_history()
    {
        var ct = TestContext.Current.CancellationToken;
        var asset = await YahooAssetAsync("ITUB4.SA", loaded: true, ct);
        var gone = new DateOnly(2001, 1, 2);
        await StoreAsync(asset, ct, [new(gone, 170m, 1m), .. Days(new(2026, 9, 10), new(2026, 9, 21)).Select(day => new DailyClose(day, 44m, 43m))]);
        yahoo.Respond = (_, from, to) => [.. Days(from > new DateOnly(2026, 9, 10) ? from : new(2026, 9, 10), to).Select(day => new DailyClose(day, 44m, 42.9m))];

        await SyncAsync(ct);

        Assert.Equal([("ITUB4.SA", new DateOnly(2026, 9, 14), Yesterday), ("ITUB4.SA", WholeHistory, Yesterday)], yahoo.Calls);
        await using var db = Context();
        var prices = await PricesAsync(db, asset, ct);
        Assert.Equal([gone, .. Days(new(2026, 9, 10), Yesterday)], prices.Select(price => price.Date));
        Assert.All(prices.Skip(1), price => Assert.Equal((44m, (decimal?)42.9m), (price.Close, price.AdjustedClose)));
        var stored = await db.Set<MarketAsset>().AsNoTracking().SingleAsync(entity => entity.Id == asset.Id, ct);
        Assert.Equal((Now, (DateOnly?)null), (stored.HistoryLoadedAt, stored.PricesRevisedFrom));
    }

    /// <summary>Spec test 20's first half: a corrected close marks where the snapshots must be rebuilt from.</summary>
    [Fact]
    public async Task A_corrected_close_marks_its_day_for_the_snapshots()
    {
        var ct = TestContext.Current.CancellationToken;
        var asset = await YahooAssetAsync("BBAS3.SA", loaded: true, ct);
        await StoreAsync(asset, ct, [.. Days(new(2026, 9, 1), new(2026, 9, 21)).Select(day => new DailyClose(day, 20m, 20m))]);
        yahoo.Respond = (_, from, to) =>
            [.. Days(from > new DateOnly(2026, 9, 1) ? from : new(2026, 9, 1), to).Select(day => new DailyClose(day, day == new DateOnly(2026, 9, 18) ? 21m : 20m, 20m))];

        await SyncAsync(ct);

        await using var db = Context();
        Assert.Equal(
            new DateOnly(2026, 9, 18),
            (await db.Set<MarketAsset>().AsNoTracking().SingleAsync(entity => entity.Id == asset.Id, ct)).PricesRevisedFrom);
    }

    /// <summary>Spec test 21: a series stored from 2021 gets 1994 to 2021 once.</summary>
    [Fact]
    public async Task A_bcb_series_synced_with_five_years_reaches_back_to_1994_once()
    {
        var ct = TestContext.Current.CancellationToken;
        options.Bcb.Series["CDI"] = new SgsSeries { Code = 12, Unit = BenchmarkUnit.PercentPerDay };
        options.Bcb.HistoryStart = new DateOnly(1994, 7, 1);
        await EnsureDatabaseAsync(ct);
        await using (var db = Context())
        {
            await new MarketDataStore(db).UpsertBenchmarkAsync("CDI", [new(new(2021, 9, 24), 0.02m), new(new(2026, 9, 22), 0.05m)], ct);
        }

        bcb.Respond = (_, from, to) => [new(from, 0.04m), new(to, 0.04m)];

        await SyncAsync(ct);
        Assert.Equal([("CDI", new DateOnly(1994, 7, 1), new DateOnly(2021, 9, 23)), ("CDI", Yesterday, Yesterday)], bcb.Calls);

        bcb.Calls.Clear();
        await SyncAsync(ct);
        Assert.Empty(bcb.Calls);
    }

    /// <summary>Spec test 22: a price benchmark from Yahoo is its total-return series, whole.</summary>
    [Fact]
    public async Task A_yahoo_price_benchmark_stores_the_adjusted_close_from_its_whole_history()
    {
        var ct = TestContext.Current.CancellationToken;
        options.PriceBenchmarks["IVVB11"] = new PriceBenchmark { Provider = ProviderKind.Yahoo, Symbol = "IVVB11.SA", Unit = BenchmarkUnit.Level };
        yahoo.Respond = (_, _, _) => [new(new(2014, 4, 29), 55m, 54.5m), new(Yesterday, 450m)];
        await EnsureDatabaseAsync(ct);

        await SyncAsync(ct);

        Assert.Equal([("IVVB11.SA", WholeHistory, Yesterday)], yahoo.Calls);
        await using var db = Context();
        Assert.Equal(
            [54.5m, 450m],
            await db.Set<Benchmark>().Where(value => value.Code == "IVVB11").OrderBy(value => value.Date).Select(value => value.Value).ToListAsync(ct));
    }

    private static IEnumerable<DateOnly> Days(DateOnly from, DateOnly to) =>
        Enumerable.Range(0, Math.Max(0, to.DayNumber - from.DayNumber + 1)).Select(from.AddDays);

    private static Task<List<Price>> PricesAsync(AppDbContext db, MarketAsset asset, CancellationToken ct) =>
        db.Set<Price>().AsNoTracking().Where(price => price.MarketAssetId == asset.Id).OrderBy(price => price.Date).ToListAsync(ct);

    private async Task StoreAsync(MarketAsset asset, CancellationToken ct, IReadOnlyList<DailyClose> closes)
    {
        await using var db = Context();
        await new MarketDataStore(db).UpsertPricesAsync(asset.Id, closes, ct);
    }

    private async Task<SyncRun> SyncAsync(CancellationToken ct)
    {
        await EnsureDatabaseAsync(ct);
        await using var db = Context();
        var sync = new MarketDataSync(
            db,
            new MarketDataStore(db),
            new PriceProviderRegistry([yahoo]),
            bcb,
            Microsoft.Extensions.Options.Options.Create(options),
            new FixedClock(Now),
            NullLogger<MarketDataSync>.Instance);

        return await sync.RunAsync(SyncTrigger.Scheduled, ct);
    }

    /// <summary>A Yahoo catalogue row; <paramref name="loaded"/> false is a new or just-edited one.</summary>
    private async Task<MarketAsset> YahooAssetAsync(string symbol, bool loaded, CancellationToken ct)
    {
        await EnsureDatabaseAsync(ct);
        var asset = new MarketAsset
        {
            Id = Guid.NewGuid(),
            Ticker = symbol.Split('.')[0],
            Name = symbol,
            Class = MarketAssetClass.StockBr,
            Currency = "BRL",
            Provider = ProviderKind.Yahoo,
            ProviderSymbol = symbol,
            IsActive = true,
            HistoryLoadedAt = loaded ? Now.AddDays(-30) : null,
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

    /// <summary>Only what a test configures is synced: no BCB series, no price benchmark.</summary>
    private static MarketDataOptions NoBcb(MarketDataOptions options)
    {
        options.Bcb.Series.Clear();
        options.PriceBenchmarks.Clear();
        return options;
    }
}
