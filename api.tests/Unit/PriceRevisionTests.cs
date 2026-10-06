using Finance.Api.Application.MarketData;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// Spec 025 tests 11 and 12: whether a fresh read of stored days disagrees with them, and the
/// earliest stored close a write changes.
/// </summary>
public sealed class PriceRevisionTests
{
    private static readonly DateOnly Day1 = new(2026, 9, 28);

    private static DateOnly Day(int n) => Day1.AddDays(n - 1);

    [Theory]
    [InlineData("100", "100.00000001", false)] // below a millionth
    [InlineData("100", "100.0001", false)] // a millionth of 100 exactly
    [InlineData("100", "100.00011", true)] // a hair beyond it
    [InlineData("44.26181793", "44.261817932128906", false)] // Yahoo's float, against the column's 8 decimals
    [InlineData("44.28", "44.261817932128906", true)] // a dividend's rebase
    [InlineData("0.00000002", "0.00000003", false)] // within 2e-8, where a millionth is nothing
    [InlineData("0.0000001", "0.0000004", true)]
    public void Values_differ_beyond_a_millionth_or_2e_8(string stored, string fetched, bool differs)
    {
        Assert.Equal(differs, PriceRevision.Differs(decimal.Parse(stored, System.Globalization.CultureInfo.InvariantCulture),
            decimal.Parse(fetched, System.Globalization.CultureInfo.InvariantCulture)));
    }

    [Fact]
    public void A_missing_adjusted_close_differs_from_one_and_two_missing_do_not()
    {
        Assert.True(PriceRevision.Differs(null, 1m));
        Assert.True(PriceRevision.Differs(1m, null));
        Assert.False(PriceRevision.Differs((decimal?)null, null));
    }

    [Fact]
    public void A_stored_day_read_again_with_another_adjusted_close_or_close_is_a_revision()
    {
        var stored = new Dictionary<DateOnly, StoredPrice> { [Day(1)] = new(10m, 9.5m), [Day(2)] = new(11m, 10.5m) };

        Assert.True(PriceRevision.Revised(stored, [new(Day(1), 10m, 9.4m), new(Day(2), 11m, 10.5m)]));
        Assert.True(PriceRevision.Revised(stored, [new(Day(2), 11.5m, 10.5m)]));
        Assert.False(PriceRevision.Revised(stored, [new(Day(1), 10m, 9.5m), new(Day(2), 11m, 10.5m), new(Day(3), 12m, 11.5m)]));
    }

    [Fact]
    public void The_earliest_change_is_a_changed_close()
    {
        var stored = Closes((1, 10m), (2, 11m), (3, 12m));

        Assert.Equal(Day(2), PriceRevision.EarliestChange(stored, [new(Day(1), 10m), new(Day(2), 11.5m), new(Day(3), 13m)], replacing: false));
    }

    [Fact]
    public void A_day_filled_in_or_older_history_is_a_change()
    {
        Assert.Equal(Day(2), PriceRevision.EarliestChange(Closes((1, 10m), (3, 12m)), [new(Day(2), 11m)], replacing: false));
        Assert.Equal(Day(1), PriceRevision.EarliestChange(Closes((5, 10m), (6, 12m)), [new(Day(1), 9m), new(Day(5), 10m)], replacing: false));
    }

    [Fact]
    public void A_stored_day_missing_from_the_read_is_a_change_only_when_replacing()
    {
        var stored = Closes((1, 10m), (2, 11m), (3, 12m));
        IReadOnlyList<DailyClose> fetched = [new(Day(1), 10m), new(Day(3), 12m)];

        Assert.Equal(Day(2), PriceRevision.EarliestChange(stored, fetched, replacing: true));
        Assert.Null(PriceRevision.EarliestChange(stored, fetched, replacing: false));
    }

    [Fact]
    public void New_days_after_the_latest_stored_one_and_an_empty_store_change_nothing()
    {
        Assert.Null(PriceRevision.EarliestChange(
            Closes((1, 10m), (2, 11m)), [new(Day(1), 10m), new(Day(2), 11m), new(Day(3), 12m)], replacing: true));
        Assert.Null(PriceRevision.EarliestChange(Closes(), [new(Day(1), 10m)], replacing: true));
    }

    private static Dictionary<DateOnly, decimal> Closes(params (int Day, decimal Close)[] closes) =>
        closes.ToDictionary(close => Day(close.Day), close => close.Close);
}
