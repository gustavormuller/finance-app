using System.Net;
using System.Net.Http.Json;
using Finance.Api.Domain.Investments;
using Microsoft.EntityFrameworkCore;
using static Finance.Api.Tests.Integration.InvestmentsApi;
using static Finance.Api.Tests.Integration.TransactionsFixtures;
using PositionItem = Finance.Api.Tests.Integration.InvestmentAssetEndpointTests.PositionItem;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 007 checkpoint 3b: positions against the calculator, the summary and <c>POST /rebuild</c>.
/// Spec integration tests 18, 25 and 26, and the definition of done's "truncated and
/// rebuilt via POST /rebuild with identical results".
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class InvestmentSummaryTests(PostgresFixture postgres)
{
    private static readonly DateOnly D = Today.AddDays(-10);

    private sealed record SummaryItem(decimal TotalBrl, decimal TotalCostBrl, decimal UnrealisedBrl);

    /// <summary>Spec integration test 25, for a BRL asset: the same movements through the calculator.</summary>
    [Fact]
    public async Task The_position_matches_the_calculator_for_the_same_movements()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct);
        var user = await api.SignInAsync("investor", ct);
        var petr4 = await api.CatalogueAsync("PETR4", ct, "BRL", (D, 10m), (D.AddDays(7), 12.34m));
        var asset = await api.HoldAsync(user.Id, petr4, ct);
        object[] bodies =
        [
            new { date = D, kind = "Buy", quantity = 100m, unitPrice = 10m, fees = 5m },
            new { date = D.AddDays(2), kind = "Buy", quantity = 33.333m, unitPrice = 11.12345678m, fees = 1.99m },
            new { date = D.AddDays(4), kind = "Sell", quantity = 40m, unitPrice = 12.5m, fees = 3.1m },
            new { date = D.AddDays(6), kind = "Jcp", amount = 17.35m, fees = 0.5m },
        ];
        foreach (var body in bodies)
        {
            using var posted = await user.Client.SendAsync(Post($"/api/investments/assets/{asset.Id}/movements", body), ct);
            Assert.Equal(HttpStatusCode.Created, posted.StatusCode);
        }

        var position = Assert.Single((await user.Client.GetFromJsonAsync<List<PositionItem>>("/api/investments/assets", ct))!);

        await using var context = api.Context(user.Id);
        var expected = PositionCalculator.Calculate(await context.Movements.ToListAsync(ct))[^1].Position;
        var latest = await context.PortfolioDaily.OrderByDescending(row => row.Date).FirstAsync(ct);
        Assert.Equal(expected.Quantity, position.Quantity);
        Assert.Equal(Math.Round(expected.AverageCost, 8, MidpointRounding.ToEven), position.AverageCost);
        Assert.Equal(Math.Round(expected.RealisedGain, 2, MidpointRounding.ToEven), position.RealisedBrl);
        Assert.Equal(16.85m, position.DividendsBrl);
        Assert.Equal((12.34m, D.AddDays(7), latest.ValueBrl, latest.CostBasisBrl), (position.Price, position.PriceDate, position.ValueBrl, position.CostBasisBrl));
        Assert.Equal(latest.ValueBrl - latest.CostBasisBrl, position.UnrealisedBrl);
        Assert.Equal(Math.Round((latest.ValueBrl - latest.CostBasisBrl) / latest.CostBasisBrl, 4, MidpointRounding.ToEven), position.UnrealisedPct);
    }

    /// <summary>Test 25 for a USD asset: realised gain and dividends converted at each event's own rate.</summary>
    [Fact]
    public async Task A_usd_position_converts_each_event_at_its_own_dates_rate()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct);
        var user = await api.SignInAsync("investor", ct);
        var aapl = await api.CatalogueAsync("AAPL", ct, "USD", (D, 100m));
        await api.UsdBrlAsync(ct, (D, 5m), (D.AddDays(5), 5.5m), (D.AddDays(8), 6m));
        var asset = await api.HoldAsync(
            user.Id, aapl, ct, Buy(D, 10m, 100m, 1m), Sell(D.AddDays(5), 4m, 120m, 2m), Dividend(D.AddDays(8), 3m));
        await using (var context = api.Context(user.Id))
        {
            await new Finance.Api.Application.Investments.SnapshotRebuild(context, TimeProvider.System).RebuildAsync(asset.Id, D, ct);
        }

        var position = Assert.Single((await user.Client.GetFromJsonAsync<List<PositionItem>>("/api/investments/assets", ct))!);

        // Average 100.1 USD; realised (480 - 2) - 100.1 x 4 = 77.6 USD at 5.5; dividend 3 USD at 6.
        Assert.Equal((6m, 100.1m, 426.8m, 18m), (position.Quantity, position.AverageCost, position.RealisedBrl, position.DividendsBrl));
        Assert.Equal((3600m, 3003m, 597m, 0.1988m), (position.ValueBrl, position.CostBasisBrl, position.UnrealisedBrl, position.UnrealisedPct));
    }

    /// <summary>Spec integration tests 26 and 18.</summary>
    [Fact]
    public async Task The_summary_totals_the_latest_row_of_each_asset_and_is_zero_for_another_user()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct);
        var userA = await api.SignInAsync("holder", ct);
        var userB = await api.SignInAsync("empty", ct);
        var petr4 = await api.HoldAsync(userA.Id, await api.CatalogueAsync("PETR4", ct, "BRL", (D, 10m), (D.AddDays(3), 11m)), ct, Buy(D, 100m, 10m, 5m));
        var vale3 = await api.HoldAsync(userA.Id, await api.CatalogueAsync("VALE3", ct, "BRL", (D, 60m)), ct, Buy(D.AddDays(1), 10m, 61m));
        await using (var context = api.Context(userA.Id))
        {
            var rebuild = new Finance.Api.Application.Investments.SnapshotRebuild(context, TimeProvider.System);
            await rebuild.RebuildAsync(petr4.Id, D, ct);
            await rebuild.RebuildAsync(vale3.Id, D, ct);
        }

        var summaryA = await userA.Client.GetFromJsonAsync<SummaryItem>("/api/investments/summary", ct);
        var summaryB = await userB.Client.GetFromJsonAsync<SummaryItem>("/api/investments/summary", ct);

        await using var asA = api.Context(userA.Id);
        var latest = await asA.PortfolioDaily.Where(row => row.Date == Today).ToListAsync(ct);
        Assert.Equal(2, latest.Count);
        Assert.Equal(latest.Sum(row => row.ValueBrl), summaryA!.TotalBrl);
        Assert.Equal(new SummaryItem(1700m, 1615m, 85m), summaryA);
        Assert.Equal(new SummaryItem(0m, 0m, 0m), summaryB);
    }

    private sealed record FxItem(decimal Rate, DateOnly Date);

    private sealed record AllocationItem(string Class, decimal ValueBrl, decimal Share);

    /// <summary>016's summary: 007's totals, the allocation by class and the latest USDBRL.</summary>
    private sealed record FullSummaryItem(
        decimal TotalBrl, decimal TotalCostBrl, decimal UnrealisedBrl, List<AllocationItem> Allocation, FxItem? UsdBrl);

    /// <summary>Spec 016 integration test 1.</summary>
    [Fact]
    public async Task The_summary_carries_the_latest_usdbrl_rate_as_stored_for_every_user()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct);
        var userA = await api.SignInAsync("holder", ct);
        var userB = await api.SignInAsync("empty", ct);

        var before = await userA.Client.GetFromJsonAsync<FullSummaryItem>("/api/investments/summary", ct);
        Assert.Null(before!.UsdBrl);

        await api.UsdBrlAsync(ct, (D, 5.1m), (D.AddDays(3), 5.3012m), (D.AddDays(1), 5.2m));
        await api.HoldAsync(userA.Id, await api.CatalogueAsync("PETR4", ct, "BRL", (D, 10m)), ct, Buy(D, 1m, 10m));

        var summaryA = await userA.Client.GetFromJsonAsync<FullSummaryItem>("/api/investments/summary", ct);
        var summaryB = await userB.Client.GetFromJsonAsync<FullSummaryItem>("/api/investments/summary", ct);

        Assert.Equal(new FxItem(5.3012m, D.AddDays(3)), summaryA!.UsdBrl);
        Assert.Equal(new FxItem(5.3012m, D.AddDays(3)), summaryB!.UsdBrl);
    }

    /// <summary>Spec 016 integration test 2.</summary>
    [Fact]
    public async Task The_summary_allocates_the_latest_rows_by_class_with_shares_summing_to_one()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct);
        var userA = await api.SignInAsync("holder", ct);
        var userB = await api.SignInAsync("empty", ct);
        await api.UsdBrlAsync(ct, (D, 5m));
        var hglg11 = await api.CatalogueAsync("HGLG11", ct, "BRL", (D, 100m));
        await using (var catalogue = api.Context(null))
        {
            await catalogue.MarketAssets.Where(asset => asset.Id == hglg11.Id)
                .ExecuteUpdateAsync(set => set.SetProperty(asset => asset.Class, Finance.Api.Domain.MarketData.MarketAssetClass.Fii), ct);
        }

        Asset[] held =
        [
            await api.HoldAsync(userA.Id, await api.CatalogueAsync("PETR4", ct, "BRL", (D, 10m), (D.AddDays(3), 11m)), ct, Buy(D, 100m, 10m, 5m)),
            await api.HoldAsync(userA.Id, await api.CatalogueAsync("VALE3", ct, "BRL", (D, 60m)), ct, Buy(D.AddDays(1), 10m, 61m)),
            await api.HoldAsync(userA.Id, await api.CatalogueAsync("AAPL", ct, "USD", (D, 100m)), ct, Buy(D, 2m, 100m)),
            // Sold down to nothing: a class worth zero is not in the allocation.
            await api.HoldAsync(userA.Id, hglg11, ct, Buy(D, 1m, 100m), Sell(D.AddDays(1), 1m, 100m)),
        ];
        await using (var context = api.Context(userA.Id))
        {
            var rebuild = new Finance.Api.Application.Investments.SnapshotRebuild(context, TimeProvider.System);
            foreach (var asset in held)
            {
                await rebuild.RebuildAsync(asset.Id, D, ct);
            }
        }

        var summaryA = await userA.Client.GetFromJsonAsync<FullSummaryItem>("/api/investments/summary", ct);
        var summaryB = await userB.Client.GetFromJsonAsync<FullSummaryItem>("/api/investments/summary", ct);

        // 1 100 + 600 in BR stocks, 2 x 100 x 5 in US ones: 1 700 and 1 000 of 2 700. Truncated,
        // 0.6296 and 0.3703 leave a ten-thousandth, which goes to the larger remainder.
        Assert.Equal(2700m, summaryA!.TotalBrl);
        Assert.Equal(
            [new AllocationItem("StockBr", 1700m, 0.6296m), new AllocationItem("StockUs", 1000m, 0.3704m)],
            summaryA.Allocation);
        Assert.Equal(1m, summaryA.Allocation.Sum(item => item.Share));
        Assert.Empty(summaryB!.Allocation);
    }

    /// <summary>
    /// Spec 018 test 1: the summary and the positions read each asset's own latest row, per
    /// user. The rows stop on different days, and another user's later row for the same
    /// instrument counts for that user alone.
    /// </summary>
    [Fact]
    public async Task Each_asset_is_valued_at_its_own_latest_row_and_only_for_its_owner()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct);
        var userA = await api.SignInAsync("holder", ct);
        var userB = await api.SignInAsync("other", ct);
        var petr4 = await api.CatalogueAsync("PETR4", ct);
        var early = await api.HoldAsync(userA.Id, petr4, ct);
        var late = await api.HoldAsync(userA.Id, await api.CatalogueAsync("VALE3", ct), ct);
        var others = await api.HoldAsync(userB.Id, petr4, ct);
        await WriteRowsAsync(api, early, (D, 10m, 100m, 90m), (D.AddDays(2), 12m, 120m, 90m));
        await WriteRowsAsync(api, late, (D, 10m, 50m, 40m), (D.AddDays(5), 11m, 55m, 40m));
        await WriteRowsAsync(api, others, (D.AddDays(9), 999m, 999m, 1m));

        var summaryA = await userA.Client.GetFromJsonAsync<FullSummaryItem>("/api/investments/summary", ct);
        var summaryB = await userB.Client.GetFromJsonAsync<SummaryItem>("/api/investments/summary", ct);
        var positionsA = (await userA.Client.GetFromJsonAsync<List<PositionItem>>("/api/investments/assets", ct))!;
        var positionsB = (await userB.Client.GetFromJsonAsync<List<PositionItem>>("/api/investments/assets", ct))!;

        Assert.Equal((175m, 130m, 45m), (summaryA!.TotalBrl, summaryA.TotalCostBrl, summaryA.UnrealisedBrl));
        Assert.Equal([new AllocationItem("StockBr", 175m, 1m)], summaryA.Allocation);
        Assert.Equal(new SummaryItem(999m, 1m, 998m), summaryB);
        Assert.Equal(
            [("PETR4", 12m, D.AddDays(2), 120m, 90m, 30m, 0.3333m), ("VALE3", 11m, D.AddDays(5), 55m, 40m, 15m, 0.375m)],
            positionsA.Select(p => (p.Ticker, p.Price, p.PriceDate, p.ValueBrl, p.CostBasisBrl, p.UnrealisedBrl, p.UnrealisedPct)));
        Assert.Equal(
            [("PETR4", 999m, D.AddDays(9), 999m)],
            positionsB.Select(p => (p.Ticker, p.Price, p.PriceDate, p.ValueBrl)));
    }

    /// <summary>Daily rows as given: date, price, value and cost basis, all in BRL.</summary>
    private static async Task WriteRowsAsync(
        InvestmentsApi api, Asset asset, params (DateOnly Date, decimal Price, decimal ValueBrl, decimal CostBasisBrl)[] rows)
    {
        await using var context = api.Context(asset.UserId);
        context.AddRange(rows.Select(row => new PortfolioDaily
        {
            UserId = asset.UserId,
            AssetId = asset.Id,
            Date = row.Date,
            Quantity = row.ValueBrl / row.Price,
            Price = row.Price,
            PriceDate = row.Date,
            FxRate = 1m,
            ValueBrl = row.ValueBrl,
            CostBasisBrl = row.CostBasisBrl,
        }));
        await context.SaveChangesAsync(TestContext.Current.CancellationToken);
    }

    /// <summary>The definition of done: truncate, POST /rebuild, identical rows; and only the caller's.</summary>
    [Fact]
    public async Task Rebuild_restores_the_callers_truncated_rows_exactly_and_no_one_elses()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct);
        var userA = await api.SignInAsync("holder", ct);
        var userB = await api.SignInAsync("other", ct);
        var petr4 = await api.CatalogueAsync("PETR4", ct, "BRL", (D, 10m), (D.AddDays(4), 10.5m));
        await PostBuyAsync(userA, (await api.HoldAsync(userA.Id, petr4, ct)).Id, ct);
        await PostBuyAsync(userB, (await api.HoldAsync(userB.Id, petr4, ct)).Id, ct);
        await using var all = api.Context(null);
        var before = await all.PortfolioDaily.IgnoreQueryFilters().AsNoTracking()
            .Where(row => row.UserId == userA.Id).OrderBy(row => row.Date).ToListAsync(ct);
        await all.PortfolioDaily.IgnoreQueryFilters().ExecuteDeleteAsync(ct);

        using var rebuilt = await userA.Client.SendAsync(Post("/api/investments/rebuild", new { }), ct);

        Assert.Equal(HttpStatusCode.Accepted, rebuilt.StatusCode);
        var after = await all.PortfolioDaily.IgnoreQueryFilters().AsNoTracking().OrderBy(row => row.Date).ToListAsync(ct);
        Assert.Equal(11, after.Count);
        Assert.Equal(
            before.Select(row => (row.UserId, row.Date, row.Quantity, row.AverageCost, row.Price, row.PriceDate, row.FxRate, row.ValueBrl, row.CostBasisBrl)),
            after.Select(row => (row.UserId, row.Date, row.Quantity, row.AverageCost, row.Price, row.PriceDate, row.FxRate, row.ValueBrl, row.CostBasisBrl)));
    }

    private static async Task PostBuyAsync(SignedInUser user, Guid assetId, CancellationToken ct)
    {
        using var posted = await user.Client.SendAsync(Post($"/api/investments/assets/{assetId}/movements",
            new { date = D, kind = "Buy", quantity = 7m, unitPrice = 10.123m, fees = 2.5m }), ct);
        Assert.Equal(HttpStatusCode.Created, posted.StatusCode);
    }
}
