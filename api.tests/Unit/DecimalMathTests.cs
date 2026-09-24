using Finance.Api.Domain.Returns;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// 008 checkpoint 1: the <c>decimal</c> exponential, logarithm and power under every
/// returns formula. Each expected value was computed outside this code base, with
/// Python's <c>decimal</c> module at 40 significant digits, and written in by hand.
/// </summary>
public sealed class DecimalMathTests
{
    /// <summary>Relative agreement asked of every result: far past what a return needs.</summary>
    private const decimal Relative = 1e-24m;

    /// <summary>
    /// <c>decimal</c> keeps at most 28 places after the point, so a result as small as
    /// <c>exp(-20)</c> carries fewer significant digits; below this, agreement is absolute.
    /// </summary>
    private const decimal Absolute = 1e-27m;

    [Theory]
    [InlineData("1", "2.718281828459045235360287471")]
    [InlineData("-1", "0.3678794411714423215955237702")]
    [InlineData("0.5", "1.648721270700128146848650788")]
    [InlineData("10", "22026.46579480671651695790065")]
    [InlineData("-20", "0.000000002061153622438557827966")]
    [InlineData("60", "114200738981568428366295718.31")]
    public void Exp_matches_the_reference(string x, string expected) =>
        AssertClose(decimal.Parse(expected), DecimalMath.Exp(decimal.Parse(x)));

    [Fact]
    public void Exp_of_zero_is_exactly_one() => Assert.Equal(1m, DecimalMath.Exp(0m));

    [Fact]
    public void Exp_past_the_decimal_range_throws() =>
        Assert.Throws<OverflowException>(() => DecimalMath.Exp(70m));

    [Fact]
    public void Exp_far_below_the_decimal_range_is_zero() => Assert.Equal(0m, DecimalMath.Exp(-70m));

    [Theory]
    [InlineData("2", "0.6931471805599453094172321215")]
    [InlineData("0.5", "-0.6931471805599453094172321215")]
    [InlineData("10", "2.302585092994045684017991455")]
    [InlineData("0.00000000000000000001", "-46.05170185988091368035982909")]
    [InlineData("123456789", "18.63140176616801803319393335")]
    public void Ln_matches_the_reference(string x, string expected) =>
        AssertClose(decimal.Parse(expected), DecimalMath.Ln(decimal.Parse(x)));

    [Fact]
    public void Ln_of_one_is_exactly_zero() => Assert.Equal(0m, DecimalMath.Ln(1m));

    [Theory]
    [InlineData("0")]
    [InlineData("-1")]
    public void Ln_outside_its_domain_throws(string x) =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DecimalMath.Ln(decimal.Parse(x)));

    [Theory]
    [InlineData("1.06", "0.08333333333333333333333333333", "1.004867550565343037541198946")] // a monthly share of 6% a.a.
    [InlineData("1.21", "0.5", "1.1")] // 008 test 7's annualisation, exact in the reference
    [InlineData("1.1", "2.005494505494505494505494505", "1.210633821537083935436760006")] // 365/182
    [InlineData("0.5", "0.002739726027397260273972602740", "0.9981027686515946342007432617")] // 1/365
    public void Pow_matches_the_reference(string value, string exponent, string expected) =>
        AssertClose(decimal.Parse(expected), DecimalMath.Pow(decimal.Parse(value), decimal.Parse(exponent)));

    [Fact]
    public void Pow_with_a_whole_exponent_multiplies_exactly()
    {
        Assert.Equal(1.0005m * 1.0005m * 1.0005m, DecimalMath.Pow(1.0005m, 3m));
        Assert.Equal(1m / (1.1m * 1.1m), DecimalMath.Pow(1.1m, -2m));
        Assert.Equal(1m, DecimalMath.Pow(1.23m, 0m));
    }

    [Fact]
    public void Pow_of_zero_is_zero_for_a_positive_exponent() => Assert.Equal(0m, DecimalMath.Pow(0m, 0.5m));

    [Fact]
    public void Pow_of_a_negative_value_to_a_fraction_throws() =>
        Assert.Throws<ArgumentOutOfRangeException>(() => DecimalMath.Pow(-1m, 0.5m));

    private static void AssertClose(decimal expected, decimal actual)
    {
        var error = Math.Abs(actual - expected);
        var allowed = Math.Max(Math.Abs(expected) * Relative, Absolute);
        Assert.True(error <= allowed, $"expected {expected}, got {actual} (off by {error}, allowed {allowed})");
    }
}
