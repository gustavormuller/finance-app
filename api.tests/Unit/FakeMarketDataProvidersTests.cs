using Finance.Api.Application.MarketData;
using Finance.Api.Domain.MarketData;
using Finance.Api.Infrastructure.MarketData;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// 008 checkpoint 6: the E2E fakes' closes have a shape, so a return can be non-zero (spec
/// 008 E2E test 36), while the latest close stays 10, which 007's E2E reads as the value.
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

    private static async Task<IReadOnlyList<DailyClose>> Closes(ProviderKind kind, DateOnly from, DateOnly to) =>
        await FakeMarketDataProviders.Registry.For(kind).GetDailyClosesAsync("ANY", from, to, CancellationToken.None);
}
