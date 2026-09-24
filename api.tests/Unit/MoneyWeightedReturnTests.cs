using Finance.Api.Domain.Returns;
using Finance.Api.Domain.Transactions;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// 008 spec unit tests 9-14: XIRR by Newton from 10%, bisection on [-0.99, 10] when Newton
/// leaves the bracket or does not converge, ACT/365. Every expected value was found outside
/// this code base, with Python's decimal at 50 digits and with LibreOffice Calc's XIRR()
/// (24.2), which agree to the 15 digits Calc prints. Both are quoted beside each test.
/// </summary>
public sealed class MoneyWeightedReturnTests
{
    private static readonly DateOnly Day0 = new(2020, 1, 1);

    /// <summary>Spec unit test 9: 1100 / 1000 after exactly a year.</summary>
    [Fact]
    public void A_year_of_ten_percent_is_ten_percent()
    {
        AssertClose(0.10m, Xirr(Flow(0, -1000m), Flow(365, 1100m)), 1e-8m);
    }

    /// <summary>Spec unit test 10: 1.21 over 730 days is 1.1 a year.</summary>
    [Fact]
    public void Two_years_are_annualised()
    {
        AssertClose(0.10m, Xirr(Flow(0, -1000m), Flow(730, 1210m)), 1e-8m);
    }

    /// <summary>
    /// Spec unit test 11. Root of -1000 - 1000 v^(182/365) + 2300 v = 0, v = 1/(1 + r):
    /// 0.20293735497973712359836808748 (Python, 50 digits); Calc: 0.202937354979737.
    /// </summary>
    [Fact]
    public void A_second_deposit_mid_year()
    {
        AssertClose(0.20293735497973712359836808748m, Xirr(Flow(0, -1000m), Flow(182, -1000m), Flow(365, 2300m)), 1e-6m);
    }

    /// <summary>Spec unit test 12.</summary>
    [Fact]
    public void All_flows_negative_is_null() =>
        Assert.Null(MoneyWeightedReturn.Compute([Flow(0, -1000m), Flow(100, -500m)]));

    [Fact]
    public void All_flows_positive_is_null() =>
        Assert.Null(MoneyWeightedReturn.Compute([Flow(0, 1000m), Flow(100, 500m)]));

    /// <summary>
    /// Spec unit test 13. 100 in, 1000 back after a year, 1000 in again, and 10 000 back
    /// after twenty years. Newton from 10% leaves the bracket on its fifth step (to -1.03);
    /// bisection finds the only root in [-0.99, 10]: 7.8729833462074169962680920923
    /// (Python, 50 digits); Calc: 7.87298334620742.
    /// </summary>
    [Fact]
    public void Bisection_finds_the_root_newton_misses_on_a_large_late_inflow()
    {
        CashFlow[] flows = [Flow(0, -100m), Flow(365, 1000m), Flow(730, -1000m), Flow(7300, 10000m)];

        Assert.Null(MoneyWeightedReturn.Newton(flows));
        AssertClose(7.8729833462074169962680920923m, Xirr(flows), 1e-6m);
    }

    /// <summary>Spec unit test 14.</summary>
    [Fact]
    public void A_single_flow_is_null() => Assert.Null(MoneyWeightedReturn.Compute([Flow(0, -1000m)]));

    [Fact]
    public void No_flows_is_null() => Assert.Null(MoneyWeightedReturn.Compute([]));

    /// <summary>Every flow on one day: no time for a rate to act on, so no root.</summary>
    [Fact]
    public void Flows_on_one_day_only_are_null() =>
        Assert.Null(MoneyWeightedReturn.Compute([Flow(0, -1000m), Flow(0, 1100m)]));

    /// <summary>A thousandfold in a day, and a 99.9% loss in a day: roots far outside [-0.99, 10].</summary>
    [Theory]
    [InlineData(1000000)]
    [InlineData(1)]
    public void A_root_outside_the_bracket_is_null(int back) =>
        Assert.Null(MoneyWeightedReturn.Compute([Flow(0, -1000m), Flow(1, back)]));

    /// <summary>
    /// CP1's warning: over forty years, (1 + r)^(t/365) at either end of the bracket is past
    /// the decimal range (0.01^-40 and 11^40). Bisection evaluates both ends, so it must not
    /// overflow. Roots: 0.5^(1/40) - 1 = -0.017179401454748939456 and 1000^(1/40) - 1 =
    /// 0.188502227437018437730 over 14 600 days (Python); Calc: -0.0171794014547489 and
    /// 0.188502227437018.
    /// </summary>
    [Theory]
    [InlineData(500, "-0.017179401454748939456")]
    [InlineData(1000000, "0.188502227437018437730")]
    public void Forty_years_do_not_overflow_at_either_end_of_the_bracket(int back, string expected)
    {
        CashFlow[] flows = [Flow(0, -1000m), Flow(14600, back)];

        AssertClose(decimal.Parse(expected), Xirr(flows), 1e-6m);
        AssertClose(decimal.Parse(expected), MoneyWeightedReturn.Bisection(flows)!.Value.Value, 1e-6m);
    }

    [Fact]
    public void Newton_converges_on_its_own_for_an_ordinary_set() =>
        AssertClose(0.10m, MoneyWeightedReturn.Newton([Flow(0, -1000m), Flow(365, 1100m)])!.Value.Value, 1e-8m);

    [Fact]
    public void Flows_out_of_order_give_the_same_rate() =>
        Assert.Equal(
            MoneyWeightedReturn.Compute([Flow(0, -1000m), Flow(182, -1000m), Flow(365, 2300m)]),
            MoneyWeightedReturn.Compute([Flow(365, 2300m), Flow(0, -1000m), Flow(182, -1000m)]));

    [Fact]
    public void Flows_in_two_currencies_are_refused() =>
        Assert.Throws<ArgumentException>(() =>
            MoneyWeightedReturn.Compute([Flow(0, -1000m), new CashFlow(Day0.AddDays(365), new Money(1100m, "USD"))]));

    private static decimal Xirr(params CashFlow[] flows) =>
        MoneyWeightedReturn.Compute(flows)?.Value ?? throw new Xunit.Sdk.XunitException("Expected a rate, got null.");

    private static CashFlow Flow(int day, decimal amount) => new(Day0.AddDays(day), new Money(amount, "BRL"));

    private static void AssertClose(decimal expected, decimal actual, decimal tolerance) =>
        Assert.True(Math.Abs(actual - expected) <= tolerance, $"expected {expected}, got {actual}");
}
