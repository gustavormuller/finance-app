using System.Net;
using System.Net.Http.Json;
using Finance.Api.Domain.MarketData;
using static Finance.Api.Tests.Integration.InvestmentsApi;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// What <c>GET /api/compare</c> answers: spec 026 integration tests 9–11. Closes and benchmark
/// values are written by hand, so every expected figure is worked out from them; the
/// annualised rates were computed in Python <c>decimal</c> at 60 digits.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class CompareFiguresTests(PostgresFixture postgres)
{
    private static readonly DateOnly Today = new(2026, 7, 15);
    private static readonly DateOnly May29 = new(2026, 5, 29); // a Friday
    private static readonly DateOnly May31 = new(2026, 5, 31); // a Sunday

    /// <summary>A custom week, Sunday to Sunday: dates alone mean a custom period.</summary>
    private const string Week = "&from=2026-05-31&to=2026-06-07";

    private static DateOnly June(int day) => new(2026, 6, day);

    /// <summary>Spec integration test 9, in each series' own currency.</summary>
    [Fact]
    public async Task Each_series_comes_back_rebased_with_its_hand_computed_change_in_the_order_requested()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct, ReturnsFixtures.ClockOn(Today));
        var user = await api.SignInAsync("compare", ct);
        var (itub, btc) = await SeedAsync(api, ct);

        var body = await CompareAsync(user.Client, $"asset:{itub.Id},asset:{btc.Id},benchmark:CDI,benchmark:IPCA,benchmark:USDBRL", Week, ct);

        Assert.Equal("original", body.Currency);
        Assert.Equal(new ComparePeriodBody(May31, June(7), 7, false), body.Period);
        Assert.Equal(
            new CompareSeriesBody($"asset:{itub.Id}", "asset", "ITUB4", "ITUB4 name", "StockBr", null, "BRL", May29, June(5), true, 0.2m, 13449.4371985553m),
            body.Series[0]);
        Assert.Equal(
            new CompareSeriesBody("benchmark:CDI", "benchmark", null, null, null, "CDI", "BRL", May31, June(8), true, 0.0025025013m, 0.1391980665m),
            body.Series[2]);
        Assert.Equal(["BTC", "CDI", "IPCA", "USDBRL"], body.Series.Skip(1).Select(series => series.Ticker ?? series.Code));
        Assert.Equal([0.25m, 0.0025025013m, 0.005m, -0.04m], body.Series.Skip(1).Select(series => series.Change));
        Assert.Equal([(May31, June(6)), (May31, June(8)), (new DateOnly(2026, 4, 30), June(1)), (May29, June(8))],
            body.Series.Skip(1).Select(series => (series.FirstDate!.Value, series.LastDate!.Value)));

        // Every day moved some series, so every day is a point; the weekend carries ITUB4's Friday.
        Assert.Equal(Enumerable.Range(0, 8).Select(May31.AddDays), body.Points.Select(point => point.Date));
        Assert.Equal([100m, 100m, 100m, 100m, 100m], body.Points[0].Values);
        Assert.Equal([100m, 110m, 100.05m, 100.5m, 102m], body.Points[1].Values);
        Assert.Equal([120m, 120m, 100.250250m, 100.5m, 96m], body.Points[5].Values);
        Assert.Equal([120m, 125m, 100.250250m, 100.5m, 96m], body.Points[7].Values);
    }

    /// <summary>Spec integration test 9, converted: a dollar series times the day's PTAX, a real series over it.</summary>
    [Fact]
    public async Task Converting_multiplies_or_divides_by_the_ptax_of_each_day()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct, ReturnsFixtures.ClockOn(Today));
        var user = await api.SignInAsync("compare", ct);
        var (itub, btc) = await SeedAsync(api, ct);
        var series = $"asset:{itub.Id},asset:{btc.Id},benchmark:CDI,benchmark:IPCA,benchmark:USDBRL";

        var brl = await CompareAsync(user.Client, series, Week + "&currency=BRL", ct);
        var usd = await CompareAsync(user.Client, series, Week + "&currency=usd", ct);

        // BTC in reais: 100 x 5.00 on the start, 110 x 5.10, 110 x 5.20, then 125 x 4.80 at the end.
        Assert.Equal("BRL", brl.Currency);
        Assert.Equal([0.2m, 0.2m, 0.0025025013m, 0.005m, -0.04m], brl.Series.Select(one => one.Change));
        Assert.Equal([100m, 112.2m, 114.4m, 124.8m, 124.8m, 115.2m, 120m, 120m], brl.Points.Select(point => point.Values[1]));

        // In dollars: ITUB4 30 / 5.00 on the start to 36 / 4.80; CDI and IPCA over the PTAX; the dollar flat.
        Assert.Equal("USD", usd.Currency);
        Assert.Equal([0.25m, 0.25m, 0.0442734388m, 0.046875m, 0m], usd.Series.Select(one => one.Change));
        Assert.Equal([100m, 98.039216m, 105.769231m], usd.Points.Take(3).Select(point => point.Values[0]));
        Assert.All(usd.Points, point => Assert.Equal(100m, point.Values[4]));
        Assert.Equal(May29, usd.Series[0].FirstDate);
    }

    /// <summary>Spec integration test 10.</summary>
    [Fact]
    public async Task A_series_without_data_is_left_out_and_series_that_do_not_overlap_draw_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct, ReturnsFixtures.ClockOn(Today));
        var user = await api.SignInAsync("compare", ct);
        var (itub, btc) = await SeedAsync(api, ct);
        var old = await api.CatalogueAsync("OLD3", ct, "BRL", (new DateOnly(2025, 1, 2), 10m), (new DateOnly(2025, 3, 31), 11m));

        var leftOut = await CompareAsync(user.Client, $"asset:{itub.Id},asset:{old.Id}", Week, ct);
        var apart = await CompareAsync(user.Client, $"asset:{old.Id},asset:{btc.Id}", "&period=max", ct);
        var nothing = await CompareAsync(user.Client, $"asset:{old.Id},asset:{itub.Id}", "&from=2024-01-01&to=2024-12-31", ct);

        // ITUB4 alone is drawn, to its last close; OLD3 has no values and no change.
        Assert.Equal(new ComparePeriodBody(May31, June(5), 5, false), leftOut.Period);
        Assert.Equal([true, false], leftOut.Series.Select(series => series.HasData));
        Assert.Equal((null, null), (leftOut.Series[1].Change, leftOut.Series[1].Annualised));
        Assert.Equal((new DateOnly(2025, 1, 2), new DateOnly(2025, 3, 31)), (leftOut.Series[1].FirstDate, leftOut.Series[1].LastDate));
        Assert.All(leftOut.Points, point => Assert.Null(point.Values[1]));

        // OLD3 ends in 2025, before BTC begins.
        Assert.Null(apart.Period);
        Assert.Empty(apart.Points);
        Assert.Equal([true, true], apart.Series.Select(series => series.HasData));
        Assert.All(apart.Series, series => Assert.Null(series.Change));

        Assert.Null(nothing.Period);
        Assert.Equal([false, false], nothing.Series.Select(series => series.HasData));
    }

    /// <summary>Spec integration test 11: presets count back from the clock's today; Máx is the latest first day.</summary>
    [Fact]
    public async Task A_preset_and_max_resolve_their_start_against_today_and_the_series()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct, ReturnsFixtures.ClockOn(June(8)));
        var user = await api.SignInAsync("compare", ct);
        var real = await api.CatalogueAsync("REAL3", ct, "BRL",
            (new DateOnly(2025, 6, 2), 10m), (new DateOnly(2025, 12, 30), 12m), (June(5), 15m));
        var dollar = await api.CatalogueAsync("DOLL", ct, "USD", (new DateOnly(2025, 9, 1), 20m), (June(5), 25m));
        var series = $"asset:{real.Id},asset:{dollar.Id}";

        var ytd = await CompareAsync(user.Client, series, "&period=ytd", ct);
        var month = await CompareAsync(user.Client, series, "&period=1m", ct);
        var max = await CompareAsync(user.Client, series, "&period=max", ct);
        var fallback = await CompareAsync(user.Client, series, "", ct);

        // YTD's start is 31 December: REAL3 carries the 30th's 12.
        Assert.Equal(new ComparePeriodBody(new DateOnly(2025, 12, 31), June(5), 156, false), ytd.Period);
        Assert.Equal([0.25m, 0.25m], ytd.Series.Select(one => one.Change));
        Assert.Equal(0.6855614175m, ytd.Series[0].Annualised);
        Assert.Equal(new ComparePeriodBody(new DateOnly(2026, 5, 8), June(5), 28, false), month.Period);

        // Máx, and the default 5A, start on DOLL's first day, where REAL3 is still at 10: the
        // start moved, which the page says.
        Assert.Equal(new ComparePeriodBody(new DateOnly(2025, 9, 1), June(5), 277, true), max.Period);
        Assert.Equal(max.Series[1].FirstDate, max.Period!.From);
        Assert.Equal([0.5m, 0.25m], max.Series.Select(one => one.Change));
        Assert.Equal(max.Period, fallback.Period);
    }

    /// <summary>ITUB4 in reais, BTC in dollars, CDI and IPCA as rates, and PTAX, around the first week of June.</summary>
    private static async Task<(MarketAsset Itub, MarketAsset Btc)> SeedAsync(InvestmentsApi api, CancellationToken ct)
    {
        var itub = await api.CatalogueAsync("ITUB4", ct, "BRL", (May29, 30m), (June(2), 33m), (June(5), 36m));
        var btc = await api.CatalogueAsync("BTC", ct, "USD", (May31, 100m), (June(1), 110m), (June(3), 120m), (June(6), 125m));
        await api.BenchmarkAsync("CDI", ct,
            (June(1), 0.05m), (June(2), 0.05m), (June(3), 0.05m), (June(4), 0.05m), (June(5), 0.05m), (June(8), 0.05m));
        await api.BenchmarkAsync("IPCA", ct, (new DateOnly(2026, 5, 1), 0.4m), (June(1), 0.5m));
        await api.BenchmarkAsync("USDBRL", ct, (May29, 5.00m), (June(1), 5.10m), (June(2), 5.20m), (June(5), 4.80m), (June(8), 4.90m));
        return (itub, btc);
    }

    private static async Task<CompareBody> CompareAsync(HttpClient client, string series, string rest, CancellationToken ct)
    {
        using var response = await client.GetAsync($"/api/compare?series={series}{rest}", ct);
        Assert.True(response.StatusCode == HttpStatusCode.OK, $"{series}{rest}: {await response.Content.ReadAsStringAsync(ct)}");
        return (await response.Content.ReadFromJsonAsync<CompareBody>(ct))!;
    }

    private sealed record CompareBody(string Currency, ComparePeriodBody? Period, List<CompareSeriesBody> Series, List<ComparePointBody> Points);

    private sealed record ComparePeriodBody(DateOnly From, DateOnly To, int Days, bool StartMoved);

    private sealed record CompareSeriesBody(
        string Key,
        string Kind,
        string? Ticker,
        string? Name,
        string? Class,
        string? Code,
        string Currency,
        DateOnly? FirstDate,
        DateOnly? LastDate,
        bool HasData,
        decimal? Change,
        decimal? Annualised);

    private sealed record ComparePointBody(DateOnly Date, List<decimal?> Values);
}
