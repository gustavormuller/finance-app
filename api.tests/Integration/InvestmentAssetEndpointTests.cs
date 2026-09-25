using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finance.Api.Domain.MarketData;
using Microsoft.EntityFrameworkCore;
using static Finance.Api.Tests.Integration.InvestmentsApi;
using static Finance.Api.Tests.Integration.TransactionsFixtures;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// <c>/api/investments/assets</c>. The HTTP halves of spec 007 integration tests 23 and
/// 24, and the assets part of 16.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class InvestmentAssetEndpointTests(PostgresFixture postgres)
{
    internal sealed record PositionItem(
        Guid AssetId, string Ticker, string Name, string Class, string Currency, string? Nickname,
        decimal Quantity, decimal AverageCost, decimal? Price, DateOnly? PriceDate,
        decimal? ValueBrl, decimal? CostBasisBrl, decimal? UnrealisedBrl, decimal? UnrealisedPct,
        decimal? RealisedBrl, decimal? DividendsBrl);

    [Fact]
    public async Task A_catalogue_asset_is_added_and_listed_as_an_empty_position()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct);
        var user = await api.SignInAsync("assets", ct);
        var petr4 = await api.CatalogueAsync("PETR4", ct);

        using var created = await user.Client.SendAsync(
            Post("/api/investments/assets", new { marketAssetId = petr4.Id, nickname = "Longo prazo" }), ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var position = (await created.Content.ReadFromJsonAsync<PositionItem>(ct))!;
        Assert.Equal($"/api/investments/assets/{position.AssetId}", created.Headers.Location?.ToString());
        Assert.Equal(("PETR4", "PETR4 name", "StockBr", "BRL", "Longo prazo"), (position.Ticker, position.Name, position.Class, position.Currency, position.Nickname));
        Assert.Equal((0m, 0m, null, null), (position.Quantity, position.AverageCost, position.Price, position.ValueBrl));
        Assert.Equal([position], await user.Client.GetFromJsonAsync<List<PositionItem>>("/api/investments/assets", ct));
    }

    /// <summary>Spec integration test 23, over HTTP.</summary>
    [Fact]
    public async Task Adding_an_asset_already_held_is_a_409_and_another_user_may_add_it()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct);
        var userA = await api.SignInAsync("assets-a", ct);
        var userB = await api.SignInAsync("assets-b", ct);
        var petr4 = await api.CatalogueAsync("PETR4", ct);
        using var first = await userA.Client.SendAsync(Post("/api/investments/assets", new { marketAssetId = petr4.Id }), ct);

        using var again = await userA.Client.SendAsync(Post("/api/investments/assets", new { marketAssetId = petr4.Id }), ct);
        using var other = await userB.Client.SendAsync(Post("/api/investments/assets", new { marketAssetId = petr4.Id }), ct);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        Assert.Equal("Você já possui este ativo na carteira.", await DetailAsync(again, ct));
        Assert.Equal(HttpStatusCode.Created, other.StatusCode);
    }

    [Fact]
    public async Task An_asset_missing_from_the_catalogue_is_registered_in_the_same_call()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct);
        var user = await api.SignInAsync("assets", ct);
        var body = new { ticker = "vale3", @class = "StockBr", provider = "Brapi", providerSymbol = "VALE3", currency = "brl" };

        using var created = await user.Client.SendAsync(Post("/api/investments/assets", body), ct);
        using var again = await user.Client.SendAsync(Post("/api/investments/assets", body), ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var position = (await created.Content.ReadFromJsonAsync<PositionItem>(ct))!;
        Assert.Equal(("VALE3", "VALE3", "BRL"), (position.Ticker, position.Name, position.Currency));
        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        await using var context = api.Context(null);
        Assert.Equal(1, await context.Set<MarketAsset>().CountAsync(asset => asset.ProviderSymbol == "VALE3", ct));
    }

    [Fact]
    public async Task A_registration_is_validated_as_the_catalogue_route_validates_it()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct);
        var user = await api.SignInAsync("assets", ct);

        using var badCurrency = await user.Client.SendAsync(Post("/api/investments/assets",
            new { ticker = "AAPL", @class = "StockUs", provider = "TwelveData", providerSymbol = "AAPL", currency = "EUR" }), ct);
        using var unknown = await user.Client.SendAsync(Post("/api/investments/assets", new { marketAssetId = Guid.NewGuid() }), ct);

        Assert.Equal(HttpStatusCode.BadRequest, badCurrency.StatusCode);
        Assert.Equal(["currency"], await ProblemFieldsAsync(badCurrency, ct));
        Assert.Equal(HttpStatusCode.BadRequest, unknown.StatusCode);
        Assert.Equal(["marketAssetId"], await ProblemFieldsAsync(unknown, ct));
    }

    /// <summary>Spec integration test 24, over HTTP, and the assets part of 16.</summary>
    [Fact]
    public async Task An_asset_with_movements_is_a_409_on_delete_and_another_users_asset_is_a_404()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct);
        var userA = await api.SignInAsync("assets-a", ct);
        var userB = await api.SignInAsync("assets-b", ct);
        var held = await api.HoldAsync(userA.Id, await api.CatalogueAsync("PETR4", ct), ct, Buy(Today, 1m, 10m));
        var empty = await api.HoldAsync(userA.Id, await api.CatalogueAsync("VALE3", ct), ct);

        using var withMovements = await userA.Client.SendAsync(Delete($"/api/investments/assets/{held.Id}"), ct);
        using var byOther = await userB.Client.SendAsync(Delete($"/api/investments/assets/{empty.Id}"), ct);
        var seenByOther = await userB.Client.GetFromJsonAsync<List<PositionItem>>("/api/investments/assets", ct);
        using var deleted = await userA.Client.SendAsync(Delete($"/api/investments/assets/{empty.Id}"), ct);

        Assert.Equal(HttpStatusCode.Conflict, withMovements.StatusCode);
        Assert.Equal("O ativo tem movimentações. Exclua-as antes de remover o ativo.", await DetailAsync(withMovements, ct));
        Assert.Equal(HttpStatusCode.NotFound, byOther.StatusCode);
        Assert.Empty(seenByOther!);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        var remaining = await userA.Client.GetFromJsonAsync<List<PositionItem>>("/api/investments/assets", ct);
        Assert.Equal([held.Id], remaining!.Select(position => position.AssetId));
    }

    internal static async Task<string?> DetailAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
        return problem.RootElement.GetProperty("detail").GetString();
    }
}
