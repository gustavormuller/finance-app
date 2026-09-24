using System.Net;
using Finance.Api.Application.MarketData;
using Finance.Api.Infrastructure.MarketData;
using static Finance.Api.Tests.Unit.MarketDataProviderHarness;

namespace Finance.Api.Tests.Unit;

/// <summary>006 spec tests 5, 7, 8 and the parsing half of 9 for CoinGecko, never the network.</summary>
public sealed class CoinGeckoProviderTests
{
    private static readonly DateTimeOffset Now = new(2024, 1, 5, 15, 0, 0, TimeSpan.Zero);
    private static readonly DateOnly From = new(2024, 1, 1);
    private static readonly DateOnly To = new(2024, 1, 5);

    [Fact]
    public async Task Market_chart_is_parsed_with_timestamps_as_utc_days()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, Fixture("coingecko-bitcoin-market-chart.json"));

        var closes = await Provider(handler).GetDailyClosesAsync("bitcoin", From, To, CancellationToken.None);

        // The trailing point is "now" (14:23 UTC on the 5th); the day keeps its 00:00 UTC point.
        Assert.Equal(
            [new(new(2024, 1, 1), 42261.04118877823m), new(new(2024, 1, 2), 44162.69181025238m),
             new(new(2024, 1, 3), 44957.96701632612m), new(new(2024, 1, 4), 42821.55808418496m),
             new DailyClose(new(2024, 1, 5), 44196.29891306401m)],
            closes);

        var request = handler.Single();
        Assert.Equal(
            "https://api.coingecko.com/api/v3/coins/bitcoin/market_chart?vs_currency=usd&days=5&interval=daily",
            request.RequestUri!.ToString());
        Assert.Equal(["coingecko-test-key"], request.Headers.GetValues("x-cg-demo-api-key"));
    }

    [Fact]
    public async Task A_millisecond_before_midnight_utc_is_still_the_same_day()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, """{"prices":[[1704153599999,1],[1704153600000,2]]}""");

        var closes = await Provider(handler).GetDailyClosesAsync("bitcoin", From, To, CancellationToken.None);

        Assert.Equal([new(new(2024, 1, 1), 1m), new DailyClose(new(2024, 1, 2), 2m)], closes);
    }

    [Fact]
    public async Task Values_reach_decimal_without_passing_through_double()
    {
        // 26 significant digits: a double keeps about 17 and would round this.
        var handler = new FakeHttpHandler(HttpStatusCode.OK, """{"prices":[[1704067200000,12345678.123456789012345678]]}""");

        var close = Assert.Single(await Provider(handler).GetDailyClosesAsync("bitcoin", From, To, CancellationToken.None));

        Assert.Equal(12345678.123456789012345678m, close.Close);
    }

    [Fact]
    public async Task Days_asked_for_are_capped_at_the_plans_history()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, """{"prices":[]}""");

        await Provider(handler).GetDailyClosesAsync("bitcoin", new DateOnly(2019, 1, 5), To, CancellationToken.None);

        Assert.Contains("&days=365&", handler.Single().RequestUri!.Query);
    }

    [Fact]
    public async Task Unknown_coin_is_empty_not_an_exception()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.NotFound, """{"error":"coin not found"}""");

        Assert.Empty(await Provider(handler).GetDailyClosesAsync("no-such-coin", From, To, CancellationToken.None));
    }

    [Fact]
    public async Task Too_many_requests_is_rate_limited_not_a_crash()
    {
        var handler = new FakeHttpHandler(
            HttpStatusCode.TooManyRequests,
            """{"status":{"error_code":429,"error_message":"You've exceeded the Rate Limit."}}""")
        {
            RetryAfter = TimeSpan.FromSeconds(60),
        };

        var error = await Assert.ThrowsAsync<ProviderRateLimitedException>(
            () => Provider(handler).GetDailyClosesAsync("bitcoin", From, To, CancellationToken.None));

        Assert.Equal("CoinGecko", error.Provider);
        Assert.Equal(TimeSpan.FromSeconds(60), error.RetryAfter);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"prices\":[[1704067200000,")]
    [InlineData("{}")]
    [InlineData("""{"prices":{}}""")]
    [InlineData("""{"prices":[[1704067200000]]}""")]
    [InlineData("""{"prices":[["2024-01-01",42261.04]]}""")]
    [InlineData("""{"prices":[[1704067200000,"42261.04"]]}""")]
    public async Task Malformed_json_is_response_invalid_and_nothing_else_escapes(string body)
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, body);

        var error = await Assert.ThrowsAsync<ProviderResponseInvalidException>(
            () => Provider(handler).GetDailyClosesAsync("bitcoin", From, To, CancellationToken.None));

        Assert.Equal("CoinGecko", error.Provider);
    }

    private static CoinGeckoProvider Provider(FakeHttpHandler handler) =>
        new(handler.Client(), Microsoft.Extensions.Options.Options.Create(Options()), new FixedClock(Now));
}
