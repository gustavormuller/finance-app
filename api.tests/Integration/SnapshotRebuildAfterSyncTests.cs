using System.Net;
using System.Net.Http.Json;
using Finance.Api.Application.MarketData;
using Finance.Api.Domain.Investments;
using Finance.Api.Domain.MarketData;
using Finance.Api.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Finance.Api.Tests.Integration.InvestmentsApi;
using static Finance.Api.Tests.Integration.TransactionsFixtures;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 007 checkpoint 3b: the rebuild after the market-data sync. Spec integration test 27, the
/// summary section (counts only: every user sees it), the gaps it fills, and its wiring
/// after a sync.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class SnapshotRebuildAfterSyncTests(PostgresFixture postgres)
{
    private static readonly DateOnly D = Today.AddDays(-10);

    private static readonly DateOnly Yesterday = Today.AddDays(-1);

    /// <summary>Spec integration test 27, invoked directly.</summary>
    [Fact]
    public async Task It_rebuilds_every_users_assets_from_yesterday_and_records_counts_only()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct);
        var userA = await api.SignInAsync("a", ct);
        var userB = await api.SignInAsync("b", ct);
        var petr4 = await api.CatalogueAsync("PETR4", ct, "BRL", (D, 10m));
        var assetA = await HoldWithRowsAsync(api, userA, petr4, ct);
        var assetB = await HoldWithRowsAsync(api, userB, petr4, ct);
        await using var all = api.Context(null);
        await all.PortfolioDaily.IgnoreQueryFilters().Where(row => row.Date >= Yesterday.AddDays(-1))
            .ExecuteUpdateAsync(set => set.SetProperty(row => row.ValueBrl, 1m), ct);
        all.Add(new Price { MarketAssetId = petr4.Id, Date = Yesterday, Close = 20m });
        var run = await SyncRunAsync(all, ct);

        await Run(api, run.Id, ct);

        var values = await all.PortfolioDaily.IgnoreQueryFilters().Where(row => row.Date >= Yesterday.AddDays(-1))
            .OrderBy(row => row.Date).Select(row => new { row.AssetId, row.Date, row.ValueBrl }).ToListAsync(ct);
        foreach (var asset in new[] { assetA, assetB })
        {
            Assert.Equal([1m, 2000m, 2000m], values.Where(row => row.AssetId == asset).Select(row => row.ValueBrl));
        }

        var stored = await all.SyncRuns.AsNoTracking().SingleAsync(ct);
        var section = SyncSummaryJson.Read(stored.Summary)[SnapshotRebuildAfterSync.SummaryKey];
        Assert.Equal((4, 2, 0, null), (section.RowsWritten, section.ItemsSynced, section.ItemsFailed, section.Error));
        Assert.Equal(1, SyncSummaryJson.Read(stored.Summary)["Brapi"].ItemsSynced);
        Assert.Equal(SyncRunStatus.Succeeded, stored.Status);
        Assert.DoesNotContain(assetA.ToString(), stored.Summary, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain(userA.Id.ToString(), stored.Summary, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>A failed asset is a count and a pt-BR sentence: no ticker, no id. The other users still rebuild.</summary>
    [Fact]
    public async Task A_failing_asset_is_counted_without_naming_it_and_does_not_stop_the_others()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct);
        var userA = await api.SignInAsync("a", ct);
        var userB = await api.SignInAsync("b", ct);
        var huge = await api.CatalogueAsync("HUGE3", ct, "BRL", (D, 1m), (Yesterday, 9_999_999_999m));
        await api.HoldAsync(userA.Id, huge, ct, Buy(D, 9_999_999_999m, 1m));
        await HoldWithRowsAsync(api, userB, await api.CatalogueAsync("PETR4", ct, "BRL", (D, 10m)), ct);
        await using var all = api.Context(null);
        var run = await SyncRunAsync(all, ct);

        await Run(api, run.Id, ct);

        var stored = await all.SyncRuns.AsNoTracking().SingleAsync(ct);
        var section = SyncSummaryJson.Read(stored.Summary)[SnapshotRebuildAfterSync.SummaryKey];
        Assert.Equal((1, 1), (section.ItemsSynced, section.ItemsFailed));
        Assert.Equal("Não foi possível recalcular as posições de um ou mais ativos.", section.Error);
        Assert.Empty(section.Failures);
        Assert.DoesNotContain("HUGE3", stored.Summary, StringComparison.Ordinal);
        Assert.Equal(SyncRunStatus.PartialFailure, stored.Status);
    }

    /// <summary>
    /// Rows missing before yesterday are filled too: a history backfilled after the
    /// movements were written, and nights the job missed.
    /// </summary>
    [Fact]
    public async Task It_fills_rows_missing_before_yesterday()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct);
        var user = await api.SignInAsync("a", ct);
        var backfilled = await api.CatalogueAsync("VALE3", ct, "BRL");
        var assetBackfilled = await HoldWithRowsAsync(api, user, backfilled, ct);
        var missed = await HoldWithRowsAsync(api, user, await api.CatalogueAsync("PETR4", ct, "BRL", (D, 10m)), ct);
        await using var all = api.Context(null);
        all.Add(new Price { MarketAssetId = backfilled.Id, Date = D.AddDays(3), Close = 60m });
        await all.PortfolioDaily.IgnoreQueryFilters().Where(row => row.AssetId == missed && row.Date > D.AddDays(4)).ExecuteDeleteAsync(ct);
        var run = await SyncRunAsync(all, ct);

        await Run(api, run.Id, ct);

        var rows = await all.PortfolioDaily.IgnoreQueryFilters().AsNoTracking().ToListAsync(ct);
        Assert.Equal(8, rows.Count(row => row.AssetId == assetBackfilled && row.Date >= D.AddDays(3)));
        Assert.Equal(11, rows.Count(row => row.AssetId == missed));
    }

    /// <summary>Wired after the sync: a manual run ends with the rebuild's section in its summary.</summary>
    [Fact]
    public async Task A_manual_sync_is_followed_by_the_rebuild()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct);
        var user = await api.SignInAsync("a", ct);
        var asset = await HoldWithRowsAsync(api, user, await api.CatalogueAsync("PETR4", ct, "BRL"), ct);

        using var sync = await user.Client.SendAsync(Post("/api/market-data/sync", new { }), ct);
        Assert.Equal(HttpStatusCode.Accepted, sync.StatusCode);

        await using var all = api.Context(null);
        for (var attempt = 0; attempt < 100 && !(await all.SyncRuns.AsNoTracking().SingleAsync(ct)).Summary.Contains(SnapshotRebuildAfterSync.SummaryKey); attempt++)
        {
            await Task.Delay(100, ct);
        }

        Assert.Contains(SnapshotRebuildAfterSync.SummaryKey, (await all.SyncRuns.AsNoTracking().SingleAsync(ct)).Summary);
        var rows = await all.PortfolioDaily.IgnoreQueryFilters().Where(row => row.AssetId == asset).ToListAsync(ct);
        Assert.Equal([Yesterday, Today], rows.Select(row => row.Date).Order());
        Assert.All(rows, row => Assert.Equal(1000m, row.ValueBrl));
    }

    private static async Task<Guid> HoldWithRowsAsync(InvestmentsApi api, SignedInUser user, MarketAsset marketAsset, CancellationToken ct)
    {
        var asset = await api.HoldAsync(user.Id, marketAsset, ct);
        using var posted = await user.Client.SendAsync(Post($"/api/investments/assets/{asset.Id}/movements",
            new { date = D, kind = "Buy", quantity = 100m, unitPrice = 10m }), ct);
        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);
        return asset.Id;
    }

    /// <summary>A finished sync run, as the sync leaves it, with one Brapi item synced.</summary>
    private static async Task<SyncRun> SyncRunAsync(Finance.Api.Infrastructure.AppDbContext all, CancellationToken ct)
    {
        var run = new SyncRun
        {
            Id = Guid.NewGuid(),
            StartedAt = DateTimeOffset.UtcNow,
            FinishedAt = DateTimeOffset.UtcNow,
            Trigger = SyncTrigger.Scheduled,
            Status = SyncRunStatus.Succeeded,
            Summary = SyncSummaryJson.Write(new Dictionary<string, ProviderSyncSummary> { ["Brapi"] = new() { ItemsSynced = 1, RowsWritten = 1 } }),
        };
        all.Add(run);
        await all.SaveChangesAsync(ct);
        return run;
    }

    private static async Task Run(InvestmentsApi api, Guid syncRunId, CancellationToken ct)
    {
        await using var scope = api.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SnapshotRebuildAfterSync>().RunAsync(syncRunId, ct);
    }
}
