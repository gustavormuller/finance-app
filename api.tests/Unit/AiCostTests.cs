using Finance.Api.Application.Ai;
using Finance.Api.Domain.Ai;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// 009 spec unit tests 1-4: the budget's cut-off, the cost of a call as an exact
/// <c>decimal</c>, and the rate it is converted at. The database halves of 1, 2 and 4 are
/// in <c>Integration/AiGatewayTests</c>.
/// </summary>
public sealed class AiCostTests
{
    /// <summary>Spec test 1.</summary>
    [Fact]
    public void Spent_14_99_of_15_is_allowed()
    {
        Assert.True(BudgetGuard.Allows(14.99m, 15.00m));
        Assert.True(BudgetGuard.Allows(14.9999m, 15.00m));
    }

    /// <summary>Spec test 2: the cut-off is at the budget, not past it.</summary>
    [Theory]
    [InlineData("15.00")]
    [InlineData("15.0001")]
    [InlineData("20")]
    public void Spent_at_or_over_the_budget_is_refused(string spent)
    {
        Assert.False(BudgetGuard.Allows(decimal.Parse(spent, System.Globalization.CultureInfo.InvariantCulture), 15.00m));
    }

    [Fact]
    public void A_zero_budget_refuses_the_first_call()
    {
        Assert.False(BudgetGuard.Allows(0m, 0m));
    }

    /// <summary>Spec test 3: known token counts and prices, an exact <c>decimal</c>.</summary>
    [Theory]
    [InlineData(1_000_000, 200_000, "5", "25", "5", "50.0000")]
    [InlineData(30_000, 800, "5", "25", "5.40", "0.9180")]
    [InlineData(12_345, 678, "1", "5", "5.40", "0.0850")] // 0.084969
    [InlineData(0, 0, "5", "25", "5.40", "0")]
    public void Cost_is_the_usd_price_per_million_tokens_at_the_rate_to_four_places(
        int input, int output, string inputPrice, string outputPrice, string rate, string expected)
    {
        var cost = AiCost.Brl(input, output, D(inputPrice), D(outputPrice), D(rate));

        Assert.Equal(D(expected), cost);
        Assert.Equal(4, cost.Scale);
    }

    /// <summary>Half a ten-thousandth rounds up, as the <c>numeric(10,4)</c> column would: a small call is never free.</summary>
    [Fact]
    public void Cost_rounds_half_away_from_zero_at_the_fourth_place()
    {
        Assert.Equal(0.0001m, AiCost.Brl(50, 0, 1m, 1m, 1m)); // 0.00005
        Assert.Equal(0.0000m, AiCost.Brl(49, 0, 1m, 1m, 1m)); // 0.000049
    }

    [Fact]
    public void Cost_holds_at_the_largest_token_counts()
    {
        Assert.Equal(128_849.0188m, AiCost.Brl(int.MaxValue, int.MaxValue, 5m, 25m, 2m)); // 2147483647 * 30 * 2 / 1e6
    }

    /// <summary>Spec test 4: the latest <c>USDBRL</c> when present, the fallback otherwise.</summary>
    [Fact]
    public void The_rate_is_the_latest_usdbrl_when_present_and_the_fallback_otherwise()
    {
        Assert.Equal(5.1234m, AiCost.UsdBrl(5.1234m, 5.40m));
        Assert.Equal(5.40m, AiCost.UsdBrl(null, 5.40m));
    }

    [Fact]
    public void A_usdbrl_that_is_not_positive_falls_back()
    {
        Assert.Equal(5.40m, AiCost.UsdBrl(0m, 5.40m));
        Assert.Equal(5.40m, AiCost.UsdBrl(-1m, 5.40m));
    }

    /// <summary>A call at 23:30 on the 31st in Brazil counts against that month, not the next one in UTC.</summary>
    [Theory]
    [InlineData("2026-09-01T02:30:00Z", "2026-08")]
    [InlineData("2026-09-01T03:00:00Z", "2026-09")]
    [InlineData("2026-12-31T23:59:59-03:00", "2026-12")]
    [InlineData("2027-01-01T00:00:00-03:00", "2027-01")]
    public void The_month_of_a_call_is_read_on_a_fixed_utc_minus_3_clock(string instant, string month)
    {
        Assert.Equal(month, AiCost.MonthOf(DateTimeOffset.Parse(instant, System.Globalization.CultureInfo.InvariantCulture)));
    }

    private static decimal D(string value) => decimal.Parse(value, System.Globalization.CultureInfo.InvariantCulture);
}
