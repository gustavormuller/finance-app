using Finance.Api.Domain.Returns;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// 008 spec unit tests 17-20: each benchmark type accumulated into an index, base 100 at
/// the period start. The start is the base day: a row dated on it does not compound, and
/// every row after it, up to the end, does. The index has one point per calendar day.
/// </summary>
public sealed class BenchmarkAccumulatorTests
{
    private static readonly DateOnly Start = new(2026, 3, 2);

    /// <summary>Spec unit test 17.</summary>
    [Fact]
    public void Daily_rate_compounds_each_row()
    {
        var index = Accumulate(
            [Point(1, 0.05m), Point(2, 0.05m), Point(3, 0.05m)], BenchmarkType.DailyRate, Start.AddDays(3));

        Assert.Equal(100m * 1.0005m * 1.0005m * 1.0005m, index[^1].Value);
        Assert.Equal(100.150075012500m, index[^1].Value);
    }

    /// <summary>Spec unit test 18.</summary>
    [Fact]
    public void Daily_rate_gap_day_holds_the_previous_index()
    {
        var index = Accumulate([Point(1, 0.05m), Point(3, 0.05m)], BenchmarkType.DailyRate, Start.AddDays(3));

        Assert.Equal([100m, 100.05m, 100.05m, 100.100025m], index.Select(point => point.Value));
        Assert.Equal(Enumerable.Range(0, 4).Select(Start.AddDays), index.Select(point => point.Date));
    }

    /// <summary>Spec unit test 19: IPCA 0.5% plus 6% a.a., the spread compounded monthly as 1.06^(1/12).</summary>
    [Fact]
    public void Monthly_rate_applies_the_spread_monthly()
    {
        var april = new DateOnly(2026, 4, 1);

        var index = BenchmarkAccumulator.Accumulate(
            [new DailyPoint(april, 0.5m)], BenchmarkType.MonthlyRate, Start, april, new Rate(0.06m))!;

        // 100 x 1.005 x 1.06^(1/12), from Python's decimal module at 40 digits.
        AssertClose(100.9891888318169752728904940m, index[^1].Value);
        Assert.Equal(100m, index[^2].Value);
    }

    [Fact]
    public void Monthly_rate_without_a_spread_is_the_rate_alone()
    {
        var index = Accumulate([Point(30, 0.42m)], BenchmarkType.MonthlyRate, Start.AddDays(30));

        Assert.Equal(100.42m, index[^1].Value);
    }

    /// <summary>Spec unit test 20.</summary>
    [Fact]
    public void Level_is_the_ratio_to_the_start()
    {
        var index = Accumulate([Point(0, 5.0m), Point(5, 5.5m)], BenchmarkType.Level, Start.AddDays(5));

        Assert.Equal(110m, index[^1].Value);
    }

    [Fact]
    public void Level_carries_forward_on_gaps_and_anchors_on_the_last_value_before_the_start()
    {
        var index = Accumulate([Point(-3, 4m), Point(2, 5m)], BenchmarkType.Level, Start.AddDays(4));

        Assert.Equal([100m, 100m, 125m, 125m, 125m], index.Select(point => point.Value));
    }

    [Fact]
    public void Index_starts_at_exactly_100_and_a_row_on_the_start_does_not_compound()
    {
        var index = Accumulate([Point(0, 0.05m), Point(1, 0.05m)], BenchmarkType.DailyRate, Start.AddDays(1));

        Assert.Equal(new DailyPoint(Start, 100m), index[0]);
        Assert.Equal(100.05m, index[^1].Value);
    }

    [Fact]
    public void Rows_after_the_end_are_ignored()
    {
        var index = Accumulate([Point(1, 0.05m), Point(2, 7m)], BenchmarkType.DailyRate, Start.AddDays(1));

        Assert.Equal(100.05m, index[^1].Value);
    }

    [Fact]
    public void Level_with_no_value_on_or_before_the_start_is_null() =>
        Assert.Null(BenchmarkAccumulator.Accumulate([Point(1, 5m)], BenchmarkType.Level, Start, Start.AddDays(3)));

    [Theory]
    [InlineData(BenchmarkType.DailyRate)]
    [InlineData(BenchmarkType.MonthlyRate)]
    public void A_rate_with_no_row_inside_the_period_is_null(BenchmarkType type) =>
        Assert.Null(BenchmarkAccumulator.Accumulate([Point(0, 0.05m), Point(9, 0.05m)], type, Start, Start.AddDays(3)));

    [Theory]
    [InlineData(BenchmarkType.DailyRate)]
    [InlineData(BenchmarkType.Level)]
    public void A_spread_on_anything_but_a_monthly_rate_is_refused(BenchmarkType type) =>
        Assert.Throws<ArgumentException>(() => BenchmarkAccumulator.Accumulate(
            [Point(0, 5m), Point(1, 5m)], type, Start, Start.AddDays(1), new Rate(0.06m)));

    [Fact]
    public void An_end_before_the_start_is_refused() =>
        Assert.Throws<ArgumentException>(() => BenchmarkAccumulator.Accumulate(
            [Point(0, 5m)], BenchmarkType.Level, Start, Start.AddDays(-1)));

    private static IReadOnlyList<DailyPoint> Accumulate(DailyPoint[] points, BenchmarkType type, DateOnly end) =>
        BenchmarkAccumulator.Accumulate(points, type, Start, end)
        ?? throw new Xunit.Sdk.XunitException("Expected an index, got null.");

    private static DailyPoint Point(int day, decimal value) => new(Start.AddDays(day), value);

    private static void AssertClose(decimal expected, decimal actual) =>
        Assert.True(Math.Abs(actual - expected) <= 1e-20m, $"expected {expected}, got {actual}");
}
