using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;
using static Finance.Api.Tests.Integration.InvestmentsApi;
using static Finance.Api.Tests.Integration.TransactionsFixtures;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// The movement routes and the synchronous rebuild behind them. Spec 007 integration
/// tests 16 (HTTP half), 17, 19, 20 and 21.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed partial class InvestmentMovementEndpointTests(PostgresFixture postgres)
{
    internal sealed record MovementItem(
        Guid Id, Guid AssetId, DateOnly Date, string Kind, decimal Quantity, decimal UnitPrice,
        decimal Amount, decimal Fees, string Currency, string? Notes, DateTimeOffset CreatedAt);

    internal sealed record DailyItem(
        DateOnly Date, decimal Quantity, decimal AverageCost, decimal Price, DateOnly PriceDate,
        decimal FxRate, decimal ValueBrl, decimal CostBasisBrl);

    /// <summary>Spec integration test 19.</summary>
    [Fact]
    public async Task Posting_a_buy_writes_daily_rows_from_its_date_to_today()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, user, assetId) = await HeldAsync(ct);
        await using var _ = api;

        using var created = await user.Client.SendAsync(Post($"/api/investments/assets/{assetId}/movements", ABuy(Today.AddDays(-5))), ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var movement = (await created.Content.ReadFromJsonAsync<MovementItem>(ct))!;
        Assert.Equal($"/api/investments/movements/{movement.Id}", created.Headers.Location?.ToString());
        Assert.Equal(("Buy", 100m, 10m, 5m, "BRL"), (movement.Kind, movement.Quantity, movement.UnitPrice, movement.Fees, movement.Currency));
        var rows = await DailyAsync(user, assetId, ct);
        Assert.Equal(Days(Today.AddDays(-5), Today), rows.Select(row => row.Date));
        Assert.All(rows, row => Assert.Equal((100m, 10.05m, 1005m), (row.Quantity, row.AverageCost, row.CostBasisBrl)));
        var listed = await user.Client.GetFromJsonAsync<List<MovementItem>>($"/api/investments/assets/{assetId}/movements", ct);

        // PostgreSQL keeps microseconds, so the stored CreatedAt is the posted one truncated.
        Assert.Equal([movement with { CreatedAt = default }], listed!.Select(item => item with { CreatedAt = default }));
    }

    /// <summary>Spec integration test 20.</summary>
    [Fact]
    public async Task Moving_the_buy_earlier_rebuilds_from_the_earlier_date()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, user, assetId) = await HeldAsync(ct);
        await using var _ = api;
        var movementId = await PostAsync(user, assetId, ABuy(Today.AddDays(-5)), ct);

        using var edited = await user.Client.SendAsync(Put($"/api/investments/movements/{movementId}", ABuy(Today.AddDays(-8), quantity: 50m)), ct);

        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);
        Assert.Equal(Today.AddDays(-8), (await edited.Content.ReadFromJsonAsync<MovementItem>(ct))!.Date);
        var rows = await DailyAsync(user, assetId, ct);
        Assert.Equal(Days(Today.AddDays(-8), Today), rows.Select(row => row.Date));
        Assert.All(rows, row => Assert.Equal(50m, row.Quantity));
    }

    /// <summary>Spec integration test 21.</summary>
    [Fact]
    public async Task Deleting_the_only_movement_removes_the_rows()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, user, assetId) = await HeldAsync(ct);
        await using var _ = api;
        var movementId = await PostAsync(user, assetId, ABuy(Today.AddDays(-5)), ct);

        using var deleted = await user.Client.SendAsync(Delete($"/api/investments/movements/{movementId}"), ct);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        Assert.Empty(await DailyAsync(user, assetId, ct));
        Assert.Empty((await user.Client.GetFromJsonAsync<List<MovementItem>>($"/api/investments/assets/{assetId}/movements", ct))!);
    }

    /// <summary>Spec integration tests 17 and 16: another user's asset and movement do not exist for B.</summary>
    [Fact]
    public async Task Another_users_asset_and_movements_are_404_on_every_route_and_nothing_is_written()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, userA, assetId) = await HeldAsync(ct);
        await using var _ = api;
        var movementId = await PostAsync(userA, assetId, ABuy(Today.AddDays(-5)), ct);
        var userB = await api.SignInAsync("intruder", ct);

        using var post = await userB.Client.SendAsync(Post($"/api/investments/assets/{assetId}/movements", ABuy(Today)), ct);
        using var put = await userB.Client.SendAsync(Put($"/api/investments/movements/{movementId}", ABuy(Today)), ct);
        using var delete = await userB.Client.SendAsync(Delete($"/api/investments/movements/{movementId}"), ct);
        using var movements = await userB.Client.GetAsync($"/api/investments/assets/{assetId}/movements", ct);
        using var daily = await userB.Client.GetAsync($"/api/investments/assets/{assetId}/daily", ct);

        Assert.Equal(
            [HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound, HttpStatusCode.NotFound],
            new[] { post, put, delete, movements, daily }.Select(response => response.StatusCode));
        Assert.Empty((await userB.Client.GetFromJsonAsync<List<InvestmentAssetEndpointTests.PositionItem>>("/api/investments/assets", ct))!);
        await using var context = api.Context(null);
        var stored = await context.Movements.IgnoreQueryFilters().SingleAsync(ct);
        Assert.Equal((movementId, Today.AddDays(-5), 100m), (stored.Id, stored.Date, stored.Quantity));
        Assert.Equal(6, await context.PortfolioDaily.IgnoreQueryFilters().CountAsync(ct));
    }

    /// <summary>A fresh database, a signed-in user holding PETR4 (BRL), closing at 10 from ten days ago.</summary>
    private async Task<(InvestmentsApi Api, SignedInUser User, Guid AssetId)> HeldAsync(CancellationToken ct)
    {
        var api = await StartAsync(postgres, ct);
        var user = await api.SignInAsync("investor", ct);
        var petr4 = await api.CatalogueAsync("PETR4", ct, "BRL", (Today.AddDays(-10), 10m));
        var asset = await api.HoldAsync(user.Id, petr4, ct);
        return (api, user, asset.Id);
    }

    private static object ABuy(DateOnly date, decimal quantity = 100m) =>
        new { date, kind = "Buy", quantity, unitPrice = 10m, fees = 5m, currency = "BRL" };

    private static async Task<Guid> PostAsync(SignedInUser user, Guid assetId, object body, CancellationToken ct)
    {
        using var response = await user.Client.SendAsync(Post($"/api/investments/assets/{assetId}/movements", body), ct);
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<MovementItem>(ct))!.Id;
    }

    private static async Task<List<DailyItem>> DailyAsync(SignedInUser user, Guid assetId, CancellationToken ct) =>
        (await user.Client.GetFromJsonAsync<List<DailyItem>>($"/api/investments/assets/{assetId}/daily", ct))!;

    private static IEnumerable<DateOnly> Days(DateOnly from, DateOnly to)
    {
        for (var day = from; day <= to; day = day.AddDays(1))
        {
            yield return day;
        }
    }
}
