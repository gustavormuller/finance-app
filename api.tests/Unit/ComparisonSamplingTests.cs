using Finance.Api.Application.Compare;

namespace Finance.Api.Tests.Unit;

/// <summary>Spec 026 unit test 5: which points the chart keeps, so a long range stays light.</summary>
public sealed class ComparisonSamplingTests
{
    /// <summary>Up to three years, every point.</summary>
    [Fact]
    public void Up_to_three_years_every_point_is_kept()
    {
        var dates = Days(new DateOnly(2023, 10, 31), 1097); // 1 096 days from the first to the last

        Assert.Equal(Enumerable.Range(0, dates.Count), ComparisonSampling.Keep(dates));
    }

    [Fact]
    public void Up_to_ten_years_the_last_date_of_each_iso_week_is_kept_with_the_start_and_the_end()
    {
        var dates = Days(new DateOnly(2022, 10, 5), 1500); // a Wednesday, four years and more

        var kept = ComparisonSampling.Keep(dates).Select(position => dates[position]).ToList();

        Assert.Equal(dates[0], kept[0]);
        Assert.Equal(dates[^1], kept[^1]);
        Assert.All(kept.Skip(1).SkipLast(1), date => Assert.Equal(DayOfWeek.Sunday, date.DayOfWeek));
        Assert.Equal(1 + 214 + 1, kept.Count); // the start, 214 Sundays, and the end (a Thursday)
    }

    [Fact]
    public void Weekly_points_of_a_weekday_series_are_its_fridays()
    {
        var dates = Days(new DateOnly(2020, 1, 1), 2000).Where(date => date.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday)).ToList();

        var kept = ComparisonSampling.Keep(dates).Select(position => dates[position]).ToList();

        Assert.All(kept.Skip(1).SkipLast(1), date => Assert.Equal(DayOfWeek.Friday, date.DayOfWeek));
    }

    [Fact]
    public void Past_ten_years_the_last_date_of_each_month_is_kept()
    {
        var dates = Days(new DateOnly(2014, 9, 17), 4400);

        var kept = ComparisonSampling.Keep(dates).Select(position => dates[position]).ToList();

        Assert.Equal(dates[0], kept[0]);
        Assert.Equal(dates[^1], kept[^1]);
        Assert.All(kept.Skip(1).SkipLast(1), date => Assert.Equal(1, date.AddDays(1).Day));
        Assert.Equal(kept.Skip(1).Select(date => (date.Year, date.Month)).Distinct().Count(), kept.Count - 1);
    }

    private static List<DateOnly> Days(DateOnly first, int count) => [.. Enumerable.Range(0, count).Select(first.AddDays)];
}
