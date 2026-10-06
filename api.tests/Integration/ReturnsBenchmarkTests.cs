using System.Net.Http.Json;
using static Finance.Api.Tests.Integration.InvestmentsApi;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// Spec 008 integration tests 31 and 32, the benchmarks beside the portfolio.
/// Every expected value was computed outside the code base, in Python <c>decimal</c> at 50
/// digits, and each XIRR also in LibreOffice Calc's <c>XIRR()</c>; they agree to the 15
/// digits Calc prints.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ReturnsBenchmarkTests(PostgresFixture postgres)
{
    private static readonly DateOnly June1 = new(2026, 6, 1);
    private static readonly DateOnly Today = new(2026, 7, 15);

    /// <summary>Spec integration test 32: 007 rows and 006 benchmarks in, hand-computed returns out.</summary>
    [Fact]
    public async Task The_portfolio_and_its_benchmarks_come_back_as_computed_by_hand()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct, ReturnsFixtures.ClockOn(Today));
        var user = await HoldingSinceJuneAsync(api, ct);
        await api.BenchmarkAsync("CDI", ct, (June1, 0.05m), (June1.AddDays(1), 0.05m), (June1.AddDays(2), 0.05m),
            (June1.AddDays(3), 0.05m), (Today.AddDays(1), 0.05m));
        await api.BenchmarkAsync("IPCA", ct, (June1, 0.4m), (new DateOnly(2026, 7, 1), 0.5m));
        await api.BenchmarkAsync("USDBRL", ct, (new DateOnly(2026, 5, 29), 5.0m), (new DateOnly(2026, 6, 30), 5.5m));
        await api.BenchmarkAsync("IVVB11", ct, (June1, 100m), (new DateOnly(2026, 6, 15), 110m), (new DateOnly(2026, 6, 30), 121m));

        var body = (await user.Client.GetFromJsonAsync<ReturnsBody>("/api/returns/portfolio", ct))!;

        // Base 2026-06-01 (the first contribution), 44 days. TWR (220 - 110)/100 x (240 + 5)/220 = 1.225.
        Assert.Equal(new PeriodBody(June1, Today, 44), body.Period);
        Assert.Equal(0.225m, body.Twr!.Total);
        Near(4.38429588287961150152m, body.Twr.Annualised);

        // -100 on 06-01, -110 on 06-11, +5 on 06-21, +240 on 07-15. Calc: 3.34541053669079.
        Near(3.34541053669079119869m, body.Xirr, 1e-8m);
        Near(3.34541053669079119869m - 4.38429588287961150152m, body.TimingEffect, 1e-8m);

        // CDI: the rows on 06-02..06-04 (06-01 is the base day, 07-16 is after the end).
        Near(0.001500750125m, body.Benchmarks["CDI"]!.Total);
        Near(0.01251777161743595704m, body.Benchmarks["CDI"]!.Annualised);

        // IPCA + 6%: July's 0.5% with the spread, 1.005 x 1.06^(1/12). June's row is on the base day.
        Near(0.00989188831816975273m, body.Benchmarks["IPCA6"]!.Total);
        Near(0.08508086459372448705m, body.Benchmarks["IPCA6"]!.Annualised);

        // Levels: 5.0 on 05-29 anchors USDBRL, 5.5 on 06-30 carries to the end; IVVB11 100 to 121.
        Assert.Equal(0.1m, body.Benchmarks["USDBRL"]!.Total);
        Near(1.20480983882878971169m, body.Benchmarks["USDBRL"]!.Annualised);
        Assert.Equal(0.21m, body.Benchmarks["IVVB11"]!.Total);
        Near(3.86118642539623366461m, body.Benchmarks["IVVB11"]!.Annualised);
        Assert.Null(body.Benchmarks["SELIC"]);

        Assert.Equal(45, body.Series.Count);
        // Benchmarks follow the portfolio in code order; configuration binding keeps no file order.
        Assert.Equal(["date", "portfolio", "CDI", "IPCA6", "IVVB11", "USDBRL"], body.Series[0].Keys);
        Assert.All(body.Series[0].Values.Skip(1), value => Assert.Equal(100m, value.GetDecimal()));
        var last = body.Series[^1];
        Assert.Equal(Today, last["date"].GetDateOnly());
        Assert.Equal(
            (122.5m, 100.150075m, 100.989189m, 110m, 121m),
            (last["portfolio"].GetDecimal(), last["CDI"].GetDecimal(), last["IPCA6"].GetDecimal(),
                last["USDBRL"].GetDecimal(), last["IVVB11"].GetDecimal()));
    }

    /// <summary>Spec integration test 31.</summary>
    [Fact]
    public async Task A_benchmark_without_rows_for_the_period_is_null_and_the_others_are_unaffected()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct, ReturnsFixtures.ClockOn(Today));
        var user = await HoldingSinceJuneAsync(api, ct);
        await api.BenchmarkAsync("CDI", ct, (June1.AddDays(1), 0.05m), (June1.AddDays(2), 0.05m), (June1.AddDays(3), 0.05m));

        // A level first recorded after the base day has nothing to divide by.
        await api.BenchmarkAsync("USDBRL", ct, (June1.AddDays(9), 5.0m), (Today, 5.5m));

        var body = (await user.Client.GetFromJsonAsync<ReturnsBody>("/api/returns/portfolio", ct))!;

        Near(0.001500750125m, body.Benchmarks["CDI"]!.Total);
        Assert.Equal(["CDI", "IPCA6", "IVVB11", "SELIC", "USDBRL"], body.Benchmarks.Keys.Order());
        Assert.All(body.Benchmarks.Where(pair => pair.Key != "CDI"), pair => Assert.Null(pair.Value));
        Assert.All(body.Series, point => Assert.Equal(["date", "portfolio", "CDI"], point.Keys));
        Assert.Equal(0.225m, body.Twr!.Total);
    }

    /// <summary>
    /// 10 PETR4 bought on 06-01 at 10 and 10 more on 06-11 at 11, a dividend of 5 on 06-21.
    /// Worth 100 to 06-10, 220 to 06-20 and 240 from 06-21 to today, 07-15.
    /// </summary>
    private static async Task<SignedInUser> HoldingSinceJuneAsync(InvestmentsApi api, CancellationToken ct)
    {
        var user = await api.SignInAsync("investor", ct);
        var petr4 = await api.CatalogueAsync("PETR4", ct);
        var asset = await api.HoldAsync(
            user.Id, petr4, ct, Buy(June1, 10m, 10m), Buy(June1.AddDays(10), 10m, 11m), Dividend(June1.AddDays(20), 5m));
        await api.DailyAsync(asset, June1, June1.AddDays(9), 10m, 10m, ct);
        await api.DailyAsync(asset, June1.AddDays(10), June1.AddDays(19), 20m, 11m, ct);
        await api.DailyAsync(asset, June1.AddDays(20), Today, 20m, 12m, ct);
        return user;
    }

    private static void Near(decimal expected, decimal? actual, decimal tolerance = 1e-9m)
    {
        Assert.NotNull(actual);
        Assert.InRange(actual.Value, expected - tolerance, expected + tolerance);
    }
}
