using System.Net;
using Finance.Api.Application.MarketData;
using Finance.Api.Domain.MarketData;
using Finance.Api.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using static Finance.Api.Tests.Integration.InvestmentsApi;
using static Finance.Api.Tests.Integration.TransactionsFixtures;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// Spec 025 test 20's second half: a close a sync changed (<see cref="MarketAsset.PricesRevisedFrom"/>)
/// makes the rebuild after the sync start every holder's asset at that day, through the
/// existing rebuild (ADR-011), and the mark goes once every holder is rebuilt.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class SnapshotRebuildRevisedPricesTests(PostgresFixture postgres)
{
    private static readonly DateOnly D = Today.AddDays(-10);

    [Fact]
    public async Task Holders_are_rebuilt_from_the_revised_day_and_the_mark_is_cleared()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct);
        var user = await api.SignInAsync("a", ct);
        var petr4 = await api.CatalogueAsync("PETR4", ct, "BRL", (D, 10m));
        var asset = await HoldWithRowsAsync(api, user, petr4, Buy(D, 100m, 10m), ct);
        await ReviseAsync(api, petr4, D.AddDays(2), 12m, ct);

        await RunAsync(api, ct);

        await using var all = api.Context(null);
        var values = await all.PortfolioDaily.IgnoreQueryFilters().Where(row => row.AssetId == asset)
            .OrderBy(row => row.Date).Select(row => row.ValueBrl).ToListAsync(ct);
        Assert.Equal([1000m, 1000m, .. Enumerable.Repeat(1200m, 9)], values);
        Assert.Null((await all.MarketAssets.AsNoTracking().SingleAsync(entity => entity.Id == petr4.Id, ct)).PricesRevisedFrom);
    }

    [Fact]
    public async Task A_failed_rebuild_keeps_the_mark_for_the_next_run()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct);
        var user = await api.SignInAsync("a", ct);
        var huge = await api.CatalogueAsync("HUGE3", ct, "BRL", (D, 1m));
        await HoldWithRowsAsync(api, user, huge, Buy(D, 9_999_999_999m, 1m), ct);

        // A close that values the position past numeric(18,2): its rebuild cannot be written.
        await ReviseAsync(api, huge, D.AddDays(2), 9_999_999_999m, ct);

        await RunAsync(api, ct);

        await using var all = api.Context(null);
        Assert.Equal(D.AddDays(2), (await all.MarketAssets.AsNoTracking().SingleAsync(entity => entity.Id == huge.Id, ct)).PricesRevisedFrom);
    }

    private static async Task<Guid> HoldWithRowsAsync(
        InvestmentsApi api, SignedInUser user, MarketAsset marketAsset, Domain.Investments.Movement buy, CancellationToken ct)
    {
        var asset = await api.HoldAsync(user.Id, marketAsset, ct, buy);
        using var rebuilt = await user.Client.SendAsync(Post("/api/investments/rebuild", new { }), ct);
        Assert.Equal(HttpStatusCode.Accepted, rebuilt.StatusCode);
        return asset.Id;
    }

    /// <summary>What a sync leaves after correcting a stored close: the new close and the mark.</summary>
    private static async Task ReviseAsync(InvestmentsApi api, MarketAsset marketAsset, DateOnly day, decimal close, CancellationToken ct)
    {
        await using var all = api.Context(null);
        await new MarketDataStore(all).UpsertPricesAsync(marketAsset.Id, [new(day, close)], ct);
        await all.MarketAssets.Where(entity => entity.Id == marketAsset.Id)
            .ExecuteUpdateAsync(set => set.SetProperty(entity => entity.PricesRevisedFrom, day), ct);
    }

    private static async Task RunAsync(InvestmentsApi api, CancellationToken ct)
    {
        await using var all = api.Context(null);
        var run = new SyncRun
        {
            Id = Guid.NewGuid(),
            StartedAt = DateTimeOffset.UtcNow,
            FinishedAt = DateTimeOffset.UtcNow,
            Trigger = SyncTrigger.Scheduled,
            Status = SyncRunStatus.Succeeded,
        };
        all.Add(run);
        await all.SaveChangesAsync(ct);

        await using var scope = api.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SnapshotRebuildAfterSync>().RunAsync(run.Id, ct);
    }
}
