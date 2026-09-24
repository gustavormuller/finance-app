using Finance.Api.Domain.Returns;
using Finance.Api.Domain.Transactions;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// 008 spec unit tests 15-16: <c>timingEffect = xirr - twr.annualised</c>, which is how much
/// the timing of the deposits helped or hurt against a passive investor in the same assets.
/// </summary>
/// <remarks>
/// DEVIATION, PENDING HUMAN SIGN-OFF (DEFERRED, "008 · checkpoint 3", option (a)). The spec
/// runs scenario 3 over four consecutive days. Its XIRR root is then 1 + r ≈ 5.9e-87, and its
/// mirror's is about 2.7e79. Both are outside decision 5's bracket [-0.99, 10] and the decimal
/// range, so XIRR is null and so is the timing effect. Tests 15 and 16 below keep the same
/// moves and the same TWR of exactly 0, but put the four moves on days 0, 91, 182 and 365.
/// The literal four-day scenarios are pinned too, as null.
/// XIRR expected values: Python decimal at 50 digits and LibreOffice Calc's XIRR() agree.
/// </remarks>
public sealed class TimingEffectTests
{
    private static readonly DateOnly Day0 = new(2020, 1, 1);

    /// <summary>The moves' dates, as day offsets from the base day: spread over a year (option (a)), and the spec's four days.</summary>
    private static readonly int[] OverAYear = [1, 92, 183, 366];
    private static readonly int[] FourDays = [1, 2, 3, 4];

    /// <summary>
    /// Spec unit test 15, respaced. 1000 in; +100% (2000); 10 000 in (12 000); -50% (6000).
    /// TWR = 2 x 1 x 0.5 - 1 = 0. XIRR of -1000 (day 0), -10 000 (day 182), +6000 (day 365):
    /// -0.67674684460299126926 (Python); Calc: -0.676746844602991. The timing hurt.
    /// </summary>
    [Fact]
    public void Depositing_big_before_the_loss_is_a_negative_timing_effect()
    {
        var (twr, xirr) = Returns(OverAYear, values: [1000m, 2000m, 12000m, 6000m], deposits: [1000m, 0m, 10000m, 0m]);

        Assert.Equal(0m, twr.Total.Value);
        Assert.Equal(0m, twr.Annualised!.Value.Value);
        AssertClose(-0.67674684460299126926m, xirr!.Value.Value);
        var effect = TimingEffect.Of(xirr, twr.Annualised)!.Value.Value;
        Assert.True(effect < 0m);
        AssertClose(-0.67674684460299126926m, effect);
    }

    /// <summary>
    /// Spec unit test 16, respaced mirror. 1000 in; -50% (500); 10 000 in (10 500); +100%
    /// (21 000). TWR = 0.5 x 1 x 2 - 1 = 0. XIRR of -1000, -10 000, +21 000 on days 0, 182, 365:
    /// 2.16930501414328840860 (Python); Calc: 2.16930501414329. The big deposit went in before the gain.
    /// </summary>
    [Fact]
    public void Depositing_big_before_the_gain_is_a_positive_timing_effect()
    {
        var (twr, xirr) = Returns(OverAYear, values: [1000m, 500m, 10500m, 21000m], deposits: [1000m, 0m, 10000m, 0m]);

        Assert.Equal(0m, twr.Total.Value);
        AssertClose(2.16930501414328840860m, xirr!.Value.Value);
        var effect = TimingEffect.Of(xirr, twr.Annualised)!.Value.Value;
        Assert.True(effect > 0m);
        AssertClose(2.16930501414328840860m, effect);
    }

    /// <summary>
    /// The spec's literal scenarios, four consecutive days: XIRR's root is far outside the
    /// bracket, so XIRR and the timing effect are null while TWR is still 0.
    /// </summary>
    [Theory]
    [InlineData(2000, 12000, 6000)]
    [InlineData(500, 10500, 21000)]
    public void Over_four_days_the_xirr_is_out_of_range_and_the_effect_null(int second, int third, int fourth)
    {
        var (twr, xirr) = Returns(FourDays, values: [1000m, second, third, fourth], deposits: [1000m, 0m, 10000m, 0m]);

        Assert.Equal(0m, twr.Total.Value);
        Assert.Null(xirr);
        Assert.Null(TimingEffect.Of(xirr, twr.Annualised));
    }

    [Fact]
    public void The_effect_is_xirr_less_the_annualised_twr() =>
        Assert.Equal(new Rate(0.02m), TimingEffect.Of(new Rate(0.12m), new Rate(0.10m)));

    [Fact]
    public void Either_side_missing_is_null()
    {
        Assert.Null(TimingEffect.Of(null, new Rate(0.10m)));
        Assert.Null(TimingEffect.Of(new Rate(0.10m), null));
    }

    /// <summary>
    /// TWR from a base day worth 0 and the four moves; XIRR from each deposit out on its day
    /// and the last value in on the last day.
    /// </summary>
    private static (TwrResult Twr, Rate? Xirr) Returns(int[] days, decimal[] values, decimal[] deposits)
    {
        ReturnDay[] returnDays =
        [
            new(Day0, 0m, 0m, 0m),
            .. days.Select((day, i) => new ReturnDay(Day0.AddDays(day), values[i], 0m, deposits[i])),
        ];
        CashFlow[] flows =
        [
            .. days.Select((day, i) => (day, deposit: deposits[i])).Where(move => move.deposit != 0m)
                .Select(move => new CashFlow(Day0.AddDays(move.day), new Money(-move.deposit, "BRL"))),
            new(Day0.AddDays(days[^1]), new Money(values[^1], "BRL")),
        ];

        return (TimeWeightedReturn.Compute(returnDays)!, MoneyWeightedReturn.Compute(flows));
    }

    private static void AssertClose(decimal expected, decimal actual) =>
        Assert.True(Math.Abs(actual - expected) <= 1e-6m, $"expected {expected}, got {actual}");
}
