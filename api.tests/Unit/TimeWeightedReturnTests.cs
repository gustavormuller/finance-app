using Finance.Api.Domain.Returns;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// 008 spec unit tests 1-8: daily-linked TWR, <c>r_d = (V_d + D_d - F_d) / V_(d-1) - 1</c>,
/// chained. The first day is the base day: its return is never computed, so a flow on it
/// does not count. Every expected value is worked out by hand in the comment beside it.
/// </summary>
public sealed class TimeWeightedReturnTests
{
    private static readonly DateOnly Day0 = new(2026, 3, 1);

    /// <summary>Spec unit test 1: 100 to 110 is 10%.</summary>
    [Fact]
    public void No_flows_is_the_value_ratio()
    {
        var twr = Compute(Day(0, 100m), Day(1, 110m));

        Assert.Equal(0.10m, twr.Total.Value);
    }

    /// <summary>
    /// Spec unit test 2. Prices 10, 12, 15, 12, 10, 11. A buys 100 units (1000) on day 1;
    /// B buys 50 (500) on day 1 and 50 more (500, at 10) on day 5. Both are 11/10 - 1 = 10%.
    /// </summary>
    [Fact]
    public void Twr_does_not_depend_on_when_the_money_went_in()
    {
        decimal[] prices = [10m, 12m, 15m, 12m, 10m, 11m];
        var a = prices.Select((price, i) => Day(i + 1, 100m * price, flow: i == 0 ? 1000m : 0m)).ToArray();
        var b = prices.Select((price, i) => Day(i + 1, (i < 4 ? 50m : 100m) * price, flow: i is 0 or 4 ? 500m : 0m)).ToArray();

        var twrA = Compute([Day(0, 0m), .. a]);
        var twrB = Compute([Day(0, 0m), .. b]);

        Assert.Equal(0.10m, twrA.Total.Value);
        Assert.Equal(twrA.Total, twrB.Total);
        Assert.Equal(twrA.Index, twrB.Index);
    }

    /// <summary>
    /// Spec unit test 3. Day 1: 1000 in. Day 2: +100% (2000). Day 3: 10 000 in (12 000).
    /// Day 4: -50% (6000). TWR = 2 x 1 x 0.5 - 1 = 0, though 11 000 went in and 6000 is left.
    /// </summary>
    [Fact]
    public void A_loss_on_the_big_deposit_does_not_show_in_twr()
    {
        var twr = Compute(Day(0, 0m), Day(1, 1000m, flow: 1000m), Day(2, 2000m), Day(3, 12000m, flow: 10000m), Day(4, 6000m));

        Assert.Equal(0m, twr.Total.Value);
        Assert.Equal([100m, 100m, 200m, 200m, 100m], twr.Index.Select(point => point.Value));
    }

    /// <summary>Spec unit test 4: a dividend of 5 on 100 held at 100 is 5% that day.</summary>
    [Fact]
    public void A_dividend_is_return_not_a_flow()
    {
        var twr = Compute(Day(1, 100m), Day(2, 100m), Day(3, 100m, income: 5m));

        Assert.Equal(0.05m, twr.Total.Value);
        Assert.Equal([100m, 100m, 105m], twr.Index.Select(point => point.Value));
    }

    /// <summary>Spec unit test 5: 100 to 110, then half sold at 110 (55 out, 55 left): still 10%.</summary>
    [Fact]
    public void A_sell_is_an_outflow_and_leaves_the_return_alone()
    {
        var twr = Compute(Day(1, 100m), Day(2, 110m), Day(3, 55m, flow: -55m));

        Assert.Equal(0.10m, twr.Total.Value);
        Assert.Equal([100m, 110m, 110m], twr.Index.Select(point => point.Value));
    }

    /// <summary>Spec unit test 6: a day after a zero value is skipped and holds the index.</summary>
    [Fact]
    public void A_day_after_a_zero_value_is_skipped_not_divided_by_zero()
    {
        var twr = Compute(Day(0, 0m), Day(1, 0m), Day(2, 500m, flow: 500m), Day(3, 550m));

        Assert.Equal(0.10m, twr.Total.Value);
        Assert.Equal([100m, 100m, 100m, 110m], twr.Index.Select(point => point.Value));
    }

    /// <summary>Spec unit test 7: 21% over 730 days is 1.21^(365/730) - 1 = 10% a year.</summary>
    [Fact]
    public void Twr_over_two_years_annualises()
    {
        var twr = Compute(Day(0, 100m), Day(730, 121m));

        Assert.Equal(0.21m, twr.Total.Value);
        Assert.Equal(730, twr.Days);
        AssertClose(0.10m, twr.Annualised!.Value.Value);
    }

    /// <summary>Spec unit test 8.</summary>
    [Fact]
    public void Index_starts_at_exactly_100()
    {
        var twr = Compute(Day(0, 250m), Day(1, 300m));

        Assert.Equal(new DailyPoint(Day0, 100.00000000m), twr.Index[0]);
        Assert.Equal(120m, twr.Index[1].Value);
    }

    /// <summary>
    /// Spec silent: annualised is computed for a year or less too, so the timing effect
    /// compares like with like (DEFERRED, 008 CP1). 10% over 365 days is 10% a year.
    /// </summary>
    [Fact]
    public void A_year_or_less_is_annualised_too()
    {
        var twr = Compute(Day(0, 100m), Day(365, 110m));

        Assert.Equal(0.10m, twr.Annualised!.Value.Value);
    }

    /// <summary>100% in one day is 2^365 a year, past the decimal range: null, not an overflow.</summary>
    [Fact]
    public void An_annualised_rate_past_the_decimal_range_is_null()
    {
        var twr = Compute(Day(0, 100m), Day(1, 200m));

        Assert.Equal(1m, twr.Total.Value);
        Assert.Null(twr.Annualised);
    }

    [Fact]
    public void A_single_day_is_zero_with_nothing_to_annualise()
    {
        var twr = Compute(Day(0, 100m));

        Assert.Equal(0m, twr.Total.Value);
        Assert.Equal(0, twr.Days);
        Assert.Null(twr.Annualised);
    }

    [Fact]
    public void A_total_loss_annualises_to_minus_one()
    {
        var twr = Compute(Day(0, 100m), Day(10, 0m));

        Assert.Equal(-1m, twr.Total.Value);
        Assert.Equal(-1m, twr.Annualised!.Value.Value);
    }

    [Fact]
    public void No_days_is_null() => Assert.Null(TimeWeightedReturn.Compute([]));

    [Fact]
    public void Days_out_of_order_are_sorted() =>
        Assert.Equal(0.10m, Compute(Day(1, 110m), Day(0, 100m)).Total.Value);

    [Fact]
    public void Two_days_on_one_date_are_refused() =>
        Assert.Throws<ArgumentException>(() => TimeWeightedReturn.Compute([Day(0, 100m), Day(0, 110m)]));

    private static TwrResult Compute(params ReturnDay[] days) =>
        TimeWeightedReturn.Compute(days) ?? throw new Xunit.Sdk.XunitException("Expected a result, got null.");

    private static ReturnDay Day(int day, decimal value, decimal income = 0m, decimal flow = 0m) =>
        new(Day0.AddDays(day), value, income, flow);

    private static void AssertClose(decimal expected, decimal actual) =>
        Assert.True(Math.Abs(actual - expected) <= 1e-20m, $"expected {expected}, got {actual}");
}
