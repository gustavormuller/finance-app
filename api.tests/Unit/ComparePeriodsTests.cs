using Finance.Api.Application.Compare;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// Spec 026 unit test 6: the window, start and end a period resolves to against what each
/// series covers.
/// </summary>
public sealed class ComparePeriodsTests
{
    private static readonly DateOnly Today = new(2026, 10, 31);

    /// <summary>Spec unit test 6: presets count back from today by the calendar.</summary>
    [Theory]
    [InlineData(ComparePeriodKind.OneMonth, "2026-09-30")]
    [InlineData(ComparePeriodKind.SixMonths, "2026-04-30")]
    [InlineData(ComparePeriodKind.Ytd, "2025-12-31")]
    [InlineData(ComparePeriodKind.OneYear, "2025-10-31")]
    [InlineData(ComparePeriodKind.FiveYears, "2021-10-31")]
    [InlineData(ComparePeriodKind.TenYears, "2016-10-31")]
    public void A_preset_starts_back_from_today_and_ends_today(ComparePeriodKind kind, string from) =>
        Assert.Equal(new CompareWindow(DateOnly.Parse(from), Today), ComparePeriods.Window(kind, null, null, Today));

    [Fact]
    public void Max_has_no_start_of_its_own() =>
        Assert.Equal(new CompareWindow(null, Today), ComparePeriods.Window(ComparePeriodKind.Max, null, null, Today));

    [Fact]
    public void Custom_takes_its_dates_and_defaults_the_end_to_today()
    {
        var from = new DateOnly(2020, 1, 1);
        var to = new DateOnly(2025, 1, 1);

        Assert.Equal(new CompareWindow(from, to), ComparePeriods.Window(ComparePeriodKind.Custom, from, to, Today));
        Assert.Equal(new CompareWindow(from, Today), ComparePeriods.Window(ComparePeriodKind.Custom, from, null, Today));
        Assert.Equal(new CompareWindow(null, to), ComparePeriods.Window(ComparePeriodKind.Custom, null, to, Today));
    }

    private static readonly SeriesCoverage SinceTwoThousand = new(new DateOnly(2000, 1, 3), new DateOnly(2026, 10, 30));
    private static readonly SeriesCoverage Bitcoin = new(new DateOnly(2014, 9, 17), new DateOnly(2026, 10, 31));

    [Fact]
    public void Max_starts_where_the_latest_starting_series_begins_and_ends_at_the_latest_observation()
    {
        var plan = ComparePeriods.Plan(new CompareWindow(null, Today), [SinceTwoThousand, Bitcoin]);

        Assert.Equal((CompareOutcome.Compared, new DateOnly(2014, 9, 17), Today), (plan.Outcome, plan.Start, plan.End));
        Assert.Equal([true, true], plan.HasData);
    }

    [Fact]
    public void A_preset_starts_at_its_own_start_when_every_series_has_begun()
    {
        var plan = ComparePeriods.Plan(new CompareWindow(new DateOnly(2021, 10, 31), Today), [SinceTwoThousand, Bitcoin]);

        Assert.Equal((CompareOutcome.Compared, new DateOnly(2021, 10, 31), Today), (plan.Outcome, plan.Start, plan.End));
    }

    [Fact]
    public void A_custom_end_before_the_data_ends_is_the_end()
    {
        var to = new DateOnly(2024, 6, 30);

        var plan = ComparePeriods.Plan(new CompareWindow(new DateOnly(2020, 1, 1), to), [SinceTwoThousand, Bitcoin]);

        Assert.Equal((new DateOnly(2020, 1, 1), to), (plan.Start, plan.End));
    }

    [Fact]
    public void A_series_that_does_not_cover_the_window_is_left_out_and_the_others_compare()
    {
        // IPCA's last row is August's; a month back from 31 October has none of it.
        var ipca = new SeriesCoverage(new DateOnly(1994, 6, 30), new DateOnly(2026, 8, 1));
        var never = new SeriesCoverage(null, null);

        var plan = ComparePeriods.Plan(new CompareWindow(new DateOnly(2026, 9, 30), Today), [SinceTwoThousand, ipca, never, Bitcoin]);

        Assert.Equal([true, false, false, true], plan.HasData);
        Assert.Equal((CompareOutcome.Compared, new DateOnly(2026, 9, 30), Today), (plan.Outcome, plan.Start, plan.End));
    }

    [Fact]
    public void A_series_with_a_single_observation_covers_nothing() =>
        Assert.Equal(
            [false, true],
            ComparePeriods.Plan(new CompareWindow(null, Today), [new(Today, Today), Bitcoin]).HasData);

    [Fact]
    public void No_series_covering_the_window_is_no_data()
    {
        var window = new CompareWindow(new DateOnly(1990, 1, 1), new DateOnly(1995, 1, 1));

        Assert.Equal(CompareOutcome.NoData, ComparePeriods.Plan(window, [SinceTwoThousand, Bitcoin]).Outcome);
    }

    [Fact]
    public void A_series_that_ends_before_another_begins_leaves_no_common_period()
    {
        var delisted = new SeriesCoverage(new DateOnly(2000, 1, 3), new DateOnly(2013, 12, 30));

        Assert.Equal(CompareOutcome.NoCommonPeriod, ComparePeriods.Plan(new CompareWindow(null, Today), [delisted, Bitcoin]).Outcome);
    }

    [Fact]
    public void A_series_that_ends_on_the_start_leaves_no_common_period()
    {
        var endsOnTheStart = new SeriesCoverage(new DateOnly(2000, 1, 3), new DateOnly(2014, 9, 17));

        Assert.Equal(
            CompareOutcome.NoCommonPeriod,
            ComparePeriods.Plan(new CompareWindow(null, Today), [endsOnTheStart, Bitcoin]).Outcome);
    }
}
