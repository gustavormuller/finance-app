using Finance.Api.Application.Returns;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// 008 checkpoint 4: the period a return is measured over (decision 9) and the chart's
/// sampling (at most 260 points). The integration halves are spec tests 28–30.
/// </summary>
public sealed class ReturnsPeriodTests
{
    private static readonly DateOnly MidYear = new(2026, 6, 30);
    private static readonly DateOnly FirstMovement = new(2024, 3, 15);

    private static PeriodRange? Resolve(
        ReturnsPeriodKind kind, DateOnly? from = null, DateOnly? to = null, DateOnly? lastRow = null) =>
        ReturnsPeriods.Resolve(kind, from, to, MidYear, FirstMovement, lastRow ?? MidYear);

    [Fact]
    public void Inception_runs_from_the_first_movement_to_today()
    {
        Assert.Equal(new PeriodRange(FirstMovement, MidYear), Resolve(ReturnsPeriodKind.Inception));
    }

    [Fact]
    public void Ytd_runs_from_the_first_of_january_on_a_mid_year_call()
    {
        Assert.Equal(new PeriodRange(new DateOnly(2026, 1, 1), MidYear), Resolve(ReturnsPeriodKind.Ytd));
    }

    [Fact]
    public void Twelve_months_starts_the_day_after_the_same_date_a_year_ago()
    {
        // The base day is then 2025-06-30, exactly a year before today: 365 days.
        Assert.Equal(new PeriodRange(new DateOnly(2025, 7, 1), MidYear), Resolve(ReturnsPeriodKind.TwelveMonths));
    }

    [Fact]
    public void Custom_takes_both_dates_and_defaults_to_inception_and_today()
    {
        var from = new DateOnly(2025, 2, 1);
        var to = new DateOnly(2025, 8, 31);

        Assert.Equal(new PeriodRange(from, to), Resolve(ReturnsPeriodKind.Custom, from, to));
        Assert.Equal(new PeriodRange(FirstMovement, to), Resolve(ReturnsPeriodKind.Custom, to: to));
        Assert.Equal(new PeriodRange(from, MidYear), Resolve(ReturnsPeriodKind.Custom, from));
    }

    [Fact]
    public void A_start_before_the_first_movement_is_clamped_to_it()
    {
        Assert.Equal(
            new PeriodRange(FirstMovement, MidYear),
            Resolve(ReturnsPeriodKind.Custom, new DateOnly(2020, 1, 1)));
        var thisMarch = new DateOnly(2026, 3, 1);
        Assert.Equal(
            new PeriodRange(thisMarch, MidYear),
            ReturnsPeriods.Resolve(ReturnsPeriodKind.Ytd, null, null, MidYear, thisMarch, MidYear));
    }

    [Fact]
    public void The_end_is_clamped_to_today_and_to_the_last_daily_row()
    {
        var lastRow = MidYear.AddDays(-1);

        Assert.Equal(MidYear, Resolve(ReturnsPeriodKind.Custom, to: MidYear.AddYears(1))!.To);
        Assert.Equal(lastRow, Resolve(ReturnsPeriodKind.Inception, lastRow: lastRow)!.To);
    }

    [Fact]
    public void A_period_that_ends_before_the_first_movement_is_empty()
    {
        Assert.Null(Resolve(ReturnsPeriodKind.Custom, new DateOnly(2020, 1, 1), new DateOnly(2020, 12, 31)));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(1, 1)]
    [InlineData(260, 260)]
    [InlineData(261, 131)]
    [InlineData(1814, 260)]
    [InlineData(1827, 230)]
    [InlineData(14_611, 258)]
    public void Sampling_keeps_at_most_260_points_on_a_regular_stride(int count, int kept)
    {
        var positions = SeriesSampling.Positions(count, SeriesSampling.MaximumPoints);

        Assert.Equal(kept, positions.Count);
        Assert.True(positions.Count <= 260);
        if (count > 0)
        {
            Assert.Equal(0, positions[0]);
            Assert.Equal(count - 1, positions[^1]);
            var strides = positions.Zip(positions.Skip(1), (a, b) => b - a).SkipLast(1).Distinct().ToList();
            Assert.True(strides.Count <= 1, "every step but the last is the same stride");
        }
    }

    [Fact]
    public void Sampling_is_weekly_over_a_little_under_five_years()
    {
        var positions = SeriesSampling.Positions(1814, SeriesSampling.MaximumPoints);

        Assert.Equal(7, positions[1] - positions[0]);
    }
}
