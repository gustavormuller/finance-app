using Finance.Api.Domain.MarketData;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// Spec 025 test 15: the migration applied to a database that already holds synced market
/// data. Assets already synced keep their history (no reload); IVVB11's rows go, so the next
/// sync reloads the benchmark whole from Yahoo instead of mixing two sources.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class AddYahooHistoryMigrationTests(PostgresFixture postgres)
{
    private const string PreviousMigration = "20260924082437_AddAi";

    private static readonly DateTimeOffset LastSynced = new(2026, 10, 5, 6, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Synced_assets_are_stamped_prices_are_kept_and_only_IVVB11_rows_are_deleted()
    {
        var ct = TestContext.Current.CancellationToken;
        var database = await postgres.CreateEmptyDatabaseAsync(ct);
        try
        {
            var synced = Guid.NewGuid();
            var neverSynced = Guid.NewGuid();
            await using (var before = TransactionsFixtures.ContextFor(database, null))
            {
                var migrator = before.GetService<IMigrator>();
                await migrator.MigrateAsync(PreviousMigration, ct);

                await before.Database.ExecuteSqlAsync(
                    $"""
                    INSERT INTO "MarketAssets" ("Id", "Ticker", "Name", "Class", "Currency", "Provider", "ProviderSymbol", "IsActive", "LastSyncedAt", "CreatedAt")
                    VALUES ({synced}, 'PETR4', 'Petrobras', 0, 'BRL', 0, 'PETR4', true, {LastSynced}, {LastSynced}),
                           ({neverSynced}, 'BTC', 'Bitcoin', 5, 'USD', 1, 'bitcoin', true, NULL, {LastSynced})
                    """,
                    ct);
                await before.Database.ExecuteSqlAsync(
                    $"""INSERT INTO "Prices" ("MarketAssetId", "Date", "Close") VALUES ({synced}, DATE '2026-10-02', 32.5)""", ct);
                await before.Database.ExecuteSqlAsync(
                    $"""
                    INSERT INTO "Benchmarks" ("Code", "Date", "Value")
                    VALUES ('IVVB11', DATE '2026-10-02', 450.12), ('CDI', DATE '2026-10-02', 0.05)
                    """,
                    ct);

                await migrator.MigrateAsync(targetMigration: null, ct);
            }

            await using var after = TransactionsFixtures.ContextFor(database, null);
            var assets = await after.Set<MarketAsset>().AsNoTracking().ToDictionaryAsync(asset => asset.Id, ct);
            Assert.Equal(LastSynced, assets[synced].HistoryLoadedAt);
            Assert.Null(assets[neverSynced].HistoryLoadedAt);
            Assert.All(assets.Values, asset => Assert.Null(asset.PricesRevisedFrom));

            var price = Assert.Single(await after.Set<Price>().AsNoTracking().ToListAsync(ct));
            Assert.Equal((32.5m, (decimal?)null), (price.Close, price.AdjustedClose));

            Assert.Equal(
                ["CDI"],
                await after.Set<Benchmark>().AsNoTracking().Select(value => value.Code).ToListAsync(ct));
        }
        finally
        {
            PostgresFixture.ReleaseConnections(database);
        }
    }
}
