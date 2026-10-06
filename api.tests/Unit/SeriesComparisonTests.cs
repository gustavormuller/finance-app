using Finance.Api.Domain.Compare;
using Finance.Api.Domain.Returns;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// Spec 026 unit tests 1, 2 and 4: series rebased to 100 on a common start, aligned on the
/// union of their dates and carried forward, and rate series read off 008's accumulator.
/// Every expected value is worked out by hand in the test.
/// </summary>
public sealed class SeriesComparisonTests
{
    // 2026-09-04 is a Friday; 2026-09-07 is a Monday and a Brazilian holiday, with no PTAX.
    private static readonly DateOnly Fri = new(2026, 9, 4);
    private static readonly DateOnly Sat = Fri.AddDays(1);
    private static readonly DateOnly Sun = Fri.AddDays(2);
    private static readonly DateOnly Mon = Fri.AddDays(3);
    private static readonly DateOnly Tue = Fri.AddDays(4);
    private static readonly DateOnly Wed = Fri.AddDays(5);

    /// <summary>Spec unit test 1.</summary>
    [Fact]
    public void Every_series_is_100_on_the_start_and_each_point_is_its_value_over_the_start()
    {
        var start = new DateOnly(2024, 1, 1);
        var middle = new DateOnly(2024, 6, 30);
        var end = new DateOnly(2025, 12, 31); // 730 days after the start

        var comparison = SeriesComparison.Compare(
            [[new(start, 50m), new(middle, 55m), new(end, 60.5m)], [new(start, 10m), new(middle, 8m), new(end, 12.1m)]],
            start,
            end);

        Assert.Equal([start, middle, end], comparison.Dates);
        Assert.Equal([100m, 110m, 121m], comparison.Indices[0]);
        Assert.Equal([100m, 80m, 121m], comparison.Indices[1]);

        // 1.21 over two years is 10% a year: 1.21^(365/730) = 1.1.
        Assert.All(comparison.Changes, change =>
        {
            Assert.Equal(0.21m, change.Change.Value);
            AssertClose(0.1m, change.Annualised!.Value.Value);
        });
    }

    /// <summary>Spec unit test 2.</summary>
    [Fact]
    public void Calendars_meet_on_the_union_of_their_dates_and_each_series_carries_its_last_value()
    {
        DailyPoint[] weekdays = [new(Fri, 10m), new(Mon, 11m), new(Tue, 12m)];
        DailyPoint[] everyDay = [new(Fri, 100m), new(Sat, 100m), new(Sun, 105m), new(Mon, 110m), new(Tue, 120m)];

        // Saturday's start takes Friday's close for the weekday series. Wednesday, the end, is
        // nobody's observation and still a point, carrying Tuesday's values.
        var comparison = SeriesComparison.Compare([weekdays, everyDay], Sat, Wed);

        Assert.Equal([Sat, Sun, Mon, Tue, Wed], comparison.Dates);
        Assert.Equal([100m, 100m, 110m, 120m, 120m], comparison.Indices[0]);
        Assert.Equal([100m, 105m, 110m, 120m, 120m], comparison.Indices[1]);
        Assert.Equal([0.2m, 0.2m], comparison.Changes.Select(change => change.Change.Value));
    }

    [Fact]
    public void Observations_before_the_start_or_after_the_end_are_not_points()
    {
        var comparison = SeriesComparison.Compare(
            [[new(Fri, 10m), new(Sun, 20m), new(Wed, 40m)], [new(Sat, 5m), new(Mon, 6m)]], Sun, Tue);

        Assert.Equal([Sun, Mon, Tue], comparison.Dates);
        Assert.Equal([100m, 100m, 100m], comparison.Indices[0]);
        Assert.Equal([100m, 120m, 120m], comparison.Indices[1]);
    }

    [Fact]
    public void A_series_with_nothing_on_or_before_the_start_cannot_be_rebased() =>
        Assert.Throws<ArgumentException>(() => SeriesComparison.Compare([[new(Fri, 10m)], [new(Mon, 10m)]], Sat, Tue));

    [Fact]
    public void An_end_that_is_not_after_the_start_is_refused() =>
        Assert.Throws<ArgumentException>(() => SeriesComparison.Compare([[new(Fri, 10m)], [new(Fri, 10m)]], Fri, Fri));

    /// <summary>Spec unit test 4: CDI's rows read off 008's accumulator, on their own dates.</summary>
    [Fact]
    public void A_daily_rate_is_the_accumulators_index_on_each_rows_date()
    {
        // Friday's row is before the start; the start is Saturday, base 100.
        DailyPoint[] cdi = [new(Fri, 0.05m), new(Tue, 0.05m), new(Wed, 0.05m)];

        var observations = RateIndex.Observations(cdi, BenchmarkType.DailyRate, Sat, Wed);

        Assert.Equal([new(Sat, 100m), new(Tue, 100.05m), new(Wed, 100.100025m)], observations);
        var accumulated = BenchmarkAccumulator.Accumulate(cdi, BenchmarkType.DailyRate, Sat, Wed)!;
        Assert.All(observations, point => Assert.Contains(point, accumulated));
    }

    [Fact]
    public void A_monthly_rate_compounds_on_the_first_of_its_month()
    {
        var august = new DateOnly(2026, 8, 1);
        var september = new DateOnly(2026, 9, 1);
        var start = new DateOnly(2026, 8, 15);

        var observations = RateIndex.Observations(
            [new(august, 0.5m), new(september, 0.4m)], BenchmarkType.MonthlyRate, start, new DateOnly(2026, 9, 30));

        Assert.Equal([new(start, 100m), new(september, 100.4m)], observations);
    }

    [Fact]
    public void A_rate_with_no_row_in_the_period_holds_at_100() =>
        Assert.Equal([new DailyPoint(Sat, 100m)], RateIndex.Observations([new(Fri, 0.05m)], BenchmarkType.DailyRate, Sat, Wed));

    [Fact]
    public void A_rate_index_compares_like_any_level()
    {
        var cdi = RateIndex.Observations([new(Mon, 0.05m), new(Tue, 0.05m)], BenchmarkType.DailyRate, Sat, Tue);

        var comparison = SeriesComparison.Compare([cdi, [new(Fri, 10m), new(Tue, 11m)]], Sat, Tue);

        Assert.Equal([Sat, Mon, Tue], comparison.Dates);
        Assert.Equal([100m, 100.05m, 100.100025m], comparison.Indices[0]);
        Assert.Equal(0.001000250000m, comparison.Changes[0].Change.Value);
    }

    private static void AssertClose(decimal expected, decimal actual) =>
        Assert.True(Math.Abs(actual - expected) <= 1e-20m, $"expected {expected}, got {actual}");
}
