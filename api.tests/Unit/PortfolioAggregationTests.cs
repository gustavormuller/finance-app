using Finance.Api.Domain.Investments;
using Finance.Api.Domain.Returns;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// 008 spec unit tests 25-26: the portfolio is the per-day sum of its assets' days, an asset
/// contributing zero before it starts, and its TWR chains the summed values.
/// </summary>
public sealed class PortfolioAggregationTests
{
    private static readonly DateOnly Day0 = new(2026, 3, 1);

    /// <summary>Spec unit test 25: A from day 1, B from day 3.</summary>
    [Fact]
    public void The_portfolio_is_the_sum_with_zero_before_an_asset_starts()
    {
        ReturnDay[] a = [Day(1, 100m, flow: 100m), Day(2, 110m), Day(3, 120m, income: 2m), Day(4, 130m)];
        ReturnDay[] b = [Day(3, 50m, flow: 50m), Day(4, 60m, flow: -5m)];

        var portfolio = PortfolioAggregation.Sum([b, a]);

        Assert.Equal([Day(1, 100m, flow: 100m), Day(2, 110m), Day(3, 170m, income: 2m, flow: 50m), Day(4, 190m, flow: -5m)], portfolio);
    }

    /// <summary>
    /// Spec unit test 26. A: 100, 110, 121 (+21%). B: 300, 300, 240 (-20%). The average of
    /// the two is +0.5%. The portfolio is 400, 410, 361: 361 / 400 - 1 = -9.75%.
    /// </summary>
    [Fact]
    public void Portfolio_twr_chains_the_summed_values_not_the_average_of_the_assets()
    {
        ReturnDay[] a = [Day(0, 100m), Day(1, 110m), Day(2, 121m)];
        ReturnDay[] b = [Day(0, 300m), Day(1, 300m), Day(2, 240m)];

        var twr = TimeWeightedReturn.Compute(PortfolioAggregation.Sum([a, b]))!;

        Assert.Equal(0.21m, TimeWeightedReturn.Compute(a)!.Total.Value);
        Assert.Equal(-0.20m, TimeWeightedReturn.Compute(b)!.Total.Value);
        AssertClose(-0.0975m, twr.Total.Value);
    }

    /// <summary>
    /// A holds 100, then gains 10% on day 3. B is bought for 200 on day 2 but has no close
    /// until day 3. The buy moves to day 3, so the portfolio does not drop on day 2: 10%.
    /// </summary>
    [Fact]
    public void An_asset_bought_before_its_first_close_does_not_make_the_portfolio_jump()
    {
        var a = ReturnSeries.InBrl("BRL", [Row(1, 100m), Row(2, 100m), Row(3, 110m)], [Buy(1, 100m)], []);
        var b = ReturnSeries.InBrl("BRL", [Row(3, 200m)], [Buy(2, 200m)], []);

        var twr = TimeWeightedReturn.Compute(PortfolioAggregation.Sum([a, b]))!;

        Assert.Equal([100m, 100m, 110m], twr.Index.Select(point => point.Value));
        Assert.Equal(0.10m, twr.Total.Value);
    }

    [Fact]
    public void No_assets_is_no_days() => Assert.Empty(PortfolioAggregation.Sum([]));

    private static ReturnDay Day(int day, decimal value, decimal income = 0m, decimal flow = 0m) =>
        new(Day0.AddDays(day), value, income, flow);

    private static PortfolioDaily Row(int day, decimal value) =>
        new() { Date = Day0.AddDays(day), Quantity = 1m, Price = value, FxRate = 1m, ValueBrl = value };

    private static Movement Buy(int day, decimal amount) =>
        new() { Date = Day0.AddDays(day), Kind = MovementKind.Buy, Quantity = 1m, UnitPrice = amount, Currency = "BRL" };

    private static void AssertClose(decimal expected, decimal actual) =>
        Assert.True(Math.Abs(actual - expected) <= 1e-20m, $"expected {expected}, got {actual}");
}
