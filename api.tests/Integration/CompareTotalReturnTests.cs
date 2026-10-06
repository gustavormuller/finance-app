using System.Net;
using System.Net.Http.Json;
using Finance.Api.Application.MarketData;
using static Finance.Api.Tests.Integration.InvestmentsApi;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 026 decision 4, in effect since 025: an asset is compared on its total return, the adjusted
/// close Yahoo sends, and on its traded close only where a row has no adjusted one.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class CompareTotalReturnTests(PostgresFixture postgres)
{
    private static readonly DateOnly Today = new(2026, 7, 15);

    private static readonly DateOnly May29 = new(2026, 5, 29); // a Friday

    private static readonly DateOnly June5 = new(2026, 6, 5); // a Friday

    /// <summary>A custom week, Sunday to Sunday.</summary>
    private const string Week = "&from=2026-05-31&to=2026-06-07";

    [Fact]
    public async Task A_dividend_paying_asset_follows_its_adjusted_close_and_a_row_without_one_its_close()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct, ReturnsFixtures.ClockOn(Today));
        var user = await api.SignInAsync("compare-total", ct);

        // Traded flat at 10.00 across a dividend of 0.50: the price is back where it was, and
        // the holder is 0.50 a share richer, which only the adjusted close shows.
        var dividend = await api.CatalogueAsync("DIVY3", ct);
        var plain = await api.CatalogueAsync("PLAN3", ct);
        await using (var db = api.Context(null))
        {
            var store = new MarketDataStore(db);
            await store.UpsertPricesAsync(dividend.Id, [new(May29, 10m, 9.5m), new(June5, 10m, 10m)], ct);
            await store.UpsertPricesAsync(plain.Id, [new(May29, 20m), new(June5, 21m)], ct);
        }

        using var response = await user.Client.GetAsync($"/api/compare?series=asset:{dividend.Id},asset:{plain.Id}{Week}", ct);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = (await response.Content.ReadFromJsonAsync<CompareBody>(ct))!;

        // 10 / 9.5 - 1, where the close alone says 0; PLAN3 has no adjusted close and moves 20 to 21.
        Assert.Equal([0.0526315789m, 0.05m], body.Series.Select(series => series.Change));
        Assert.Equal([105.263158m, 105m], body.Points[^1].Values);
    }

    private sealed record CompareBody(List<CompareSeriesBody> Series, List<ComparePointBody> Points);

    private sealed record CompareSeriesBody(string Key, decimal? Change);

    private sealed record ComparePointBody(DateOnly Date, List<decimal?> Values);
}
