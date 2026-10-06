using Finance.Api.Application.MarketData;
using Finance.Api.Domain.MarketData;
using Finance.Api.Infrastructure.MarketData;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// The E2E fakes' closes have a shape, so a return can be non-zero (spec 008 E2E test 36),
/// while the latest close stays 10, which 007's E2E reads as the value.
/// </summary>
public sealed class FakeMarketDataProvidersTests
{
    private static readonly DateOnly To = new(2026, 9, 23);

    [Theory]
    [InlineData(ProviderKind.Brapi)]
    [InlineData(ProviderKind.CoinGecko)]
    [InlineData(ProviderKind.TwelveData)]
    [InlineData(ProviderKind.Binance)]
    public async Task Every_day_from_from_to_to_has_a_close_and_the_close_on_to_is_10(ProviderKind kind)
    {
        var closes = await Closes(kind, To.AddDays(-9), To);

        Assert.Equal(10, closes.Count);
        Assert.Equal(Enumerable.Range(0, 10).Select(i => To.AddDays(-9 + i)), closes.Select(close => close.Date));
        Assert.Equal(new DailyClose(To, 10m), closes[^1]);
    }

    [Fact]
    public async Task A_close_is_one_cent_lower_for_each_day_before_to()
    {
        var closes = await Closes(ProviderKind.Brapi, To.AddDays(-29), To);

        Assert.Equal(9.71m, closes[0].Close); // 29 days before `to`
        Assert.Equal(9.98m, closes[^3].Close);
        Assert.Equal(9.99m, closes[^2].Close);
    }

    [Fact]
    public async Task Five_hundred_days_back_and_earlier_the_close_holds_at_5()
    {
        // The sync backfills five years; a close must never reach zero.
        var closes = await Closes(ProviderKind.Brapi, To.AddYears(-5), To);

        Assert.Equal(5m, closes[0].Close);
        Assert.Equal(5m, closes.Single(close => close.Date == To.AddDays(-500)).Close);
        Assert.Equal(5.01m, closes.Single(close => close.Date == To.AddDays(-499)).Close);
        Assert.All(closes, close => Assert.InRange(close.Close, 5m, 10m));
    }

    [Fact]
    public async Task The_same_range_gives_the_same_closes()
    {
        Assert.Equal(await Closes(ProviderKind.Brapi, To.AddDays(-40), To), await Closes(ProviderKind.Brapi, To.AddDays(-40), To));
    }

    /// <summary>
    /// 025 test 14: the Yahoo fake loads a whole history like Yahoo, ends at 12.50, and has a
    /// 2:1 split on <c>to - 100</c> and dividends on <c>to - 30</c>, <c>- 60</c> and <c>- 90</c>.
    /// </summary>
    [Fact]
    public async Task The_yahoo_fake_has_a_split_and_dividends_and_ends_at_12_50()
    {
        var yahoo = FakeMarketDataProviders.Registry.For(ProviderKind.Yahoo);

        var closes = (await yahoo.GetDailyClosesAsync("ANY", new(1900, 1, 1), To, CancellationToken.None))
            .ToDictionary(close => close.Date);

        Assert.Equal((new DateOnly(1900, 1, 1), true), (yahoo.HistoryStart, yahoo.RevisesHistory));
        Assert.Equal(1001, closes.Count);
        Assert.Equal(To.AddDays(-1000), closes.Keys.Min());
        Assert.Equal(new DailyClose(To, 12.50m, 12.50m), closes[To]);

        // The day before the split traded at twice its split-adjusted price; three dividends lie ahead of it.
        Assert.Equal(new DailyClose(To.AddDays(-101), 22.98m, 11.14873551m), closes[To.AddDays(-101)]);
        Assert.Equal(new DailyClose(To.AddDays(-100), 11.50m, 11.1584385m), closes[To.AddDays(-100)]);

        // A dividend takes 1 % off the adjusted close of every day before it, and nothing after.
        Assert.Equal(new DailyClose(To.AddDays(-31), 12.19m, 12.0681m), closes[To.AddDays(-31)]);
        Assert.Equal(new DailyClose(To.AddDays(-30), 12.20m, 12.20m), closes[To.AddDays(-30)]);
    }

    private static async Task<IReadOnlyList<DailyClose>> Closes(ProviderKind kind, DateOnly from, DateOnly to) =>
        await FakeMarketDataProviders.Registry.For(kind).GetDailyClosesAsync("ANY", from, to, CancellationToken.None);
}
