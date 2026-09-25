using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finance.Api.Application.MarketData;
using static Finance.Api.Tests.Integration.MarketDataApi;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 006 checkpoint 4: the catalogue, price and benchmark routes, and the endpoint half of
/// spec integration test 19.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class MarketDataAssetEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task A_registered_asset_is_found_by_ticker_or_name_and_the_routes_need_a_session()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, client) = await StartAsync(postgres, ct);
        await using var _ = api;

        using var created = await client.SendAsync(TransactionsFixtures.Post("/api/market-data/assets", Petr4()), ct);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var asset = (await created.Content.ReadFromJsonAsync<AssetItem>(ct))!;
        Assert.Equal(("PETR4", "Petrobras PN", "StockBr", "Brapi", "PETR4", "BRL"),
            (asset.Ticker, asset.Name, asset.Class, asset.Provider, asset.ProviderSymbol, asset.Currency));
        Assert.True(asset.IsActive);
        Assert.Null(asset.LastSyncedAt);
        Assert.Equal($"/api/market-data/assets/{asset.Id}", created.Headers.Location?.ToString());

        Assert.Equal(asset.Id, Assert.Single((await client.GetFromJsonAsync<List<AssetItem>>("/api/market-data/assets?q=petr", ct))!).Id);
        Assert.Single((await client.GetFromJsonAsync<List<AssetItem>>("/api/market-data/assets?q=petrobras", ct))!);
        Assert.Single((await client.GetFromJsonAsync<List<AssetItem>>("/api/market-data/assets", ct))!);
        Assert.Empty((await client.GetFromJsonAsync<List<AssetItem>>("/api/market-data/assets?q=VALE", ct))!);

        using var anonymous = api.Factory.CreateApiClient();
        foreach (var url in new[] { "/api/market-data/assets", "/api/market-data/benchmarks/CDI" })
        {
            using var response = await anonymous.GetAsync(url, ct);
            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }

    /// <summary>Spec integration test 19, endpoint half: the unique index answers as a 409 with a reason.</summary>
    [Fact]
    public async Task A_provider_symbol_registered_twice_is_a_409_but_another_provider_may_have_it()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, client) = await StartAsync(postgres, ct);
        await using var _ = api;
        using var first = await client.SendAsync(TransactionsFixtures.Post("/api/market-data/assets", Petr4()), ct);

        using var again = await client.SendAsync(TransactionsFixtures.Post("/api/market-data/assets", Petr4()), ct);
        using var elsewhere = await client.SendAsync(
            TransactionsFixtures.Post("/api/market-data/assets", Petr4("TwelveData", "PETR4")), ct);

        Assert.Equal(HttpStatusCode.Conflict, again.StatusCode);
        using var problem = JsonDocument.Parse(await again.Content.ReadAsStringAsync(ct));
        Assert.Contains("PETR4", problem.RootElement.GetProperty("detail").GetString());
        Assert.Equal(HttpStatusCode.Created, elsewhere.StatusCode);
    }

    /// <summary>The CoinGecko adapter asks for one quote currency, so the catalogue must say the same one.</summary>
    [Theory]
    [InlineData("USD", HttpStatusCode.Created)]
    [InlineData("usd", HttpStatusCode.Created)]
    [InlineData("BRL", HttpStatusCode.BadRequest)]
    public async Task A_CoinGecko_asset_must_be_quoted_in_its_VsCurrency(string currency, HttpStatusCode expected)
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, client) = await StartAsync(postgres, ct);
        await using var _ = api;

        using var response = await client.SendAsync(TransactionsFixtures.Post("/api/market-data/assets", new
        {
            ticker = "BTC", @class = "Crypto", provider = "CoinGecko", providerSymbol = "bitcoin", currency,
        }), ct);

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.BadRequest)
        {
            Assert.Equal(["currency"], await TransactionsFixtures.ProblemFieldsAsync(response, ct));
        }
        else
        {
            var asset = (await response.Content.ReadFromJsonAsync<AssetItem>(ct))!;
            Assert.Equal(("USD", "BTC"), (asset.Currency, asset.Name));
        }
    }

    /// <summary>019 test 21: a Binance asset is a pair quoted in reais, stored as Binance spells it.</summary>
    [Theory]
    [InlineData("BRL", "btcbrl", null)]
    [InlineData("USD", "BTCBRL", "currency")]
    [InlineData("BRL", "BTCUSDT", "providerSymbol")]
    [InlineData("BRL", "BRL", "providerSymbol")]
    public async Task A_Binance_asset_is_a_pair_quoted_in_reais(string currency, string symbol, string? refused)
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, client) = await StartAsync(postgres, ct);
        await using var _ = api;

        using var response = await client.SendAsync(TransactionsFixtures.Post("/api/market-data/assets", new
        {
            ticker = "BTC", @class = "Crypto", provider = "Binance", providerSymbol = symbol, currency,
        }), ct);

        if (refused is null)
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);
            var asset = (await response.Content.ReadFromJsonAsync<AssetItem>(ct))!;
            Assert.Equal(("Binance", "BTCBRL", "BRL"), (asset.Provider, asset.ProviderSymbol, asset.Currency));
        }
        else
        {
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal([refused], await TransactionsFixtures.ProblemFieldsAsync(response, ct));
        }
    }

    [Fact]
    public async Task Every_invalid_field_is_reported_at_once()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, client) = await StartAsync(postgres, ct);
        await using var _ = api;

        using var response = await client.SendAsync(TransactionsFixtures.Post("/api/market-data/assets", new
        {
            ticker = " ", @class = 99, provider = 7, providerSymbol = new string('x', 51), currency = "EUR",
        }), ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(
            ["class", "currency", "provider", "providerSymbol", "ticker"],
            (await TransactionsFixtures.ProblemFieldsAsync(response, ct)).Order(StringComparer.Ordinal));
    }

    [Fact]
    public async Task Prices_and_benchmarks_are_read_oldest_first_within_the_range()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, client) = await StartAsync(postgres, ct);
        await using var _ = api;
        using var created = await client.SendAsync(TransactionsFixtures.Post("/api/market-data/assets", Petr4()), ct);
        var asset = (await created.Content.ReadFromJsonAsync<AssetItem>(ct))!;
        await using (var db = api.Context())
        {
            var store = new MarketDataStore(db);
            await store.UpsertPricesAsync(asset.Id, [new(new(2026, 9, 3), 38.1m), new(new(2026, 9, 1), 37.12345678m), new(new(2026, 9, 2), 37.5m)], ct);
            await store.UpsertBenchmarkAsync("CDI", [new(new(2026, 9, 2), 0.055131m), new(new(2026, 9, 1), 0.05513m)], ct);
        }

        var prices = await client.GetFromJsonAsync<List<PriceItem>>(
            $"/api/market-data/assets/{asset.Id}/prices?from=2026-09-01&to=2026-09-02", ct);
        var all = await client.GetFromJsonAsync<List<PriceItem>>($"/api/market-data/assets/{asset.Id}/prices", ct);
        var cdi = await client.GetFromJsonAsync<List<BenchmarkItem>>("/api/market-data/benchmarks/cdi?from=2026-09-02", ct);
        using var missing = await client.GetAsync($"/api/market-data/assets/{Guid.NewGuid()}/prices", ct);

        Assert.Equal([new(new(2026, 9, 1), 37.12345678m), new(new(2026, 9, 2), 37.5m)], prices);
        Assert.Equal(3, all!.Count);
        Assert.Equal([new BenchmarkItem(new(2026, 9, 2), 0.055131m)], cdi);
        Assert.Equal(HttpStatusCode.NotFound, missing.StatusCode);
        Assert.Empty((await client.GetFromJsonAsync<List<BenchmarkItem>>("/api/market-data/benchmarks/NOPE", ct))!);
    }
}
