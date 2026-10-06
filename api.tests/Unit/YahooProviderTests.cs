using System.Net;
using Finance.Api.Application.MarketData;
using Finance.Api.Domain.MarketData;
using Finance.Api.Infrastructure.MarketData;
using Microsoft.Extensions.Time.Testing;
using static Finance.Api.Tests.Unit.MarketDataProviderHarness;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// Spec 025 tests 1 to 8: Yahoo's chart endpoint, against responses captured on 2026-10-06,
/// never the network.
/// </summary>
public sealed class YahooProviderTests
{
    /// <summary>When the fixtures were captured: Tuesday 2026-10-06, 06:56:33 UTC.</summary>
    private static readonly DateTimeOffset Captured = DateTimeOffset.FromUnixTimeSeconds(1791269793);

    [Fact]
    public async Task Closes_are_raw_traded_prices_dated_in_New_York_and_adjusted_closes_are_yahoos()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, Fixture("yahoo-chart-aapl-2020-split.json"));

        var closes = await Provider(handler).GetDailyClosesAsync("AAPL", new(2020, 8, 24), new(2020, 9, 4), CancellationToken.None);

        Assert.Equal(
            [new(2020, 8, 24), new(2020, 8, 25), new(2020, 8, 26), new(2020, 8, 27), new(2020, 8, 28),
             new(2020, 8, 31), new(2020, 9, 1), new(2020, 9, 2), new(2020, 9, 3), new DateOnly(2020, 9, 4)],
            closes.Select(close => close.Date));

        // Before the 4:1 split of 2020-08-31 Yahoo sends a quarter of the traded price.
        var lastBefore = closes.Single(close => close.Date == new DateOnly(2020, 8, 28));
        Assert.Equal(499.23m, lastBefore.Close);
        Assert.Equal(120.95569610595703m, lastBefore.AdjustedClose);

        var firstAfter = closes.Single(close => close.Date == new DateOnly(2020, 8, 31));
        Assert.Equal(129.04m, firstAfter.Close);
        Assert.Equal(125.05756378173828m, firstAfter.AdjustedClose);
    }

    [Fact]
    public async Task Bonus_shares_raise_every_earlier_close_by_their_ratio()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, Fixture("yahoo-chart-itub4-bonus-shares.json"));

        var closes = (await Provider(handler).GetDailyClosesAsync("ITUB4.SA", new(2025, 3, 12), new(2026, 1, 2), CancellationToken.None))
            .ToDictionary(close => close.Date, close => close.Close);

        // 110:100 on 2025-03-18 and 103:100 on 2025-12-26: Yahoo's 31.1915 is 35.34 traded.
        Assert.Equal(35.34m, closes[new(2025, 3, 17)]);
        Assert.Equal(32.30m, closes[new(2025, 3, 18)]);
        Assert.Equal(40.26m, closes[new(2025, 12, 23)]);
        Assert.Equal(39.10m, closes[new(2025, 12, 26)]);
        Assert.Equal(204, closes.Count);
    }

    [Fact]
    public async Task The_request_asks_from_the_day_before_from_up_to_now_with_events_and_a_browser_user_agent()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, Fixture("yahoo-chart-btcusd-today.json"));

        await Provider(handler).GetDailyClosesAsync(" btc-usd ", new(2026, 10, 1), new(2026, 10, 5), CancellationToken.None);

        var request = handler.Single();
        Assert.Equal(
            "https://query1.finance.yahoo.com/v8/finance/chart/BTC-USD"
            + "?period1=1790726400&period2=1791269793&interval=1d&events=div%2Csplit&includeAdjustedClose=true",
            request.RequestUri!.AbsoluteUri);
        Assert.Equal(Options().Yahoo.UserAgent, request.Headers.UserAgent.ToString());
        Assert.Contains("application/json", request.Headers.Accept.Select(value => value.MediaType));
    }

    [Fact]
    public async Task A_whole_history_is_asked_from_1900_and_symbols_are_escaped()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, Fixture("yahoo-chart-weekend.json"));

        await Provider(handler).GetDailyClosesAsync("^BVSP", YahooProvider.WholeHistory, new(2026, 10, 5), CancellationToken.None);

        Assert.StartsWith(
            "https://query1.finance.yahoo.com/v8/finance/chart/%5EBVSP?period1=-2208988800&period2=1791269793&",
            handler.Single().RequestUri!.AbsoluteUri);
    }

    [Theory]
    [InlineData(3600)] // read in summer, as captured
    [InlineData(0)] // read in winter
    public async Task London_midnight_bars_get_their_london_day_in_either_half_of_the_year(int gmtOffset)
    {
        // UK summer time began on 2026-03-29: bars before are stamped 00:00 UTC, after 23:00 UTC.
        var body = Fixture("yahoo-chart-brlx-dst.json").Replace("\"gmtoffset\":3600,\"timezone\"", $"\"gmtoffset\":{gmtOffset},\"timezone\"");
        var handler = new FakeHttpHandler(HttpStatusCode.OK, body);

        var closes = await Provider(handler).GetDailyClosesAsync("BRL=X", new(2026, 3, 25), new(2026, 4, 3), CancellationToken.None);

        Assert.Equal(
            [new(2026, 3, 25), new(2026, 3, 26), new(2026, 3, 27), new(2026, 3, 30), new(2026, 3, 31),
             new(2026, 4, 1), new(2026, 4, 2), new DateOnly(2026, 4, 3)],
            closes.Select(close => close.Date));
        Assert.Equal(5.2326m, closes[0].Close); // priceHint 4
    }

    [Fact]
    public async Task Todays_unfinished_session_and_null_closes_are_never_stored()
    {
        // BTC-USD: 2026-10-05 was still null and 2026-10-06 is the day being traded.
        var handler = new FakeHttpHandler(HttpStatusCode.OK, Fixture("yahoo-chart-btcusd-today.json"));

        var closes = await Provider(handler).GetDailyClosesAsync("BTC-USD", new(2026, 10, 1), new(2026, 10, 6), CancellationToken.None);

        Assert.Equal(
            [new(new(2026, 10, 1), 84853.10m, 84853.1015625m), new(new(2026, 10, 2), 84497.21m, 84497.2109375m),
             new(new(2026, 10, 3), 84763.58m, 84763.578125m), new DailyClose(new(2026, 10, 4), 86480.30m, 86480.3046875m)],
            closes);
    }

    [Fact]
    public async Task Days_after_to_are_left_out()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, Fixture("yahoo-chart-brlx-today.json"));

        var closes = await Provider(handler).GetDailyClosesAsync("BRL=X", new(2026, 9, 30), new(2026, 10, 2), CancellationToken.None);

        Assert.Equal(
            [new(new(2026, 9, 30), 5.2029m, 5.202899932861328m), new(new(2026, 10, 1), 5.1791m, 5.179100036621094m),
             new DailyClose(new(2026, 10, 2), 5.2226m, 5.222599983215332m)],
            closes);
    }

    [Fact]
    public async Task Fxs_now_point_never_stands_in_for_a_days_missing_close()
    {
        // Read a day later: 2026-10-06's bar is null, and the quote Yahoo stamped "now" on that
        // day (02:46 UTC) is no close.
        var handler = new FakeHttpHandler(HttpStatusCode.OK, Fixture("yahoo-chart-brlx-today.json"));
        var nextDay = new DateTimeOffset(2026, 10, 7, 6, 0, 0, TimeSpan.Zero);

        var closes = await Provider(handler, nextDay).GetDailyClosesAsync("BRL=X", new(2026, 9, 30), new(2026, 10, 6), CancellationToken.None);

        Assert.Equal(
            [new(2026, 9, 30), new(2026, 10, 1), new(2026, 10, 2), new DateOnly(2026, 10, 5)],
            closes.Select(close => close.Date));
    }

    [Fact]
    public async Task Without_a_price_hint_a_close_is_kept_as_sent_through_decimal()
    {
        var body = Fixture("yahoo-chart-aapl-2020-split.json").Replace("\"priceHint\":2,", "");
        var handler = new FakeHttpHandler(HttpStatusCode.OK, body);

        var closes = await Provider(handler).GetDailyClosesAsync("AAPL", new(2020, 8, 28), new(2020, 8, 31), CancellationToken.None);

        Assert.Equal([499.23001098632812m, 129.0399932861328m], closes.Select(close => close.Close));
    }

    [Fact]
    public async Task An_unknown_symbol_is_refused_by_name()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.NotFound, Fixture("yahoo-chart-unknown-symbol.json"));

        var error = await Assert.ThrowsAsync<ProviderSymbolUnknownException>(
            () => Provider(handler).GetDailyClosesAsync("nope123.sa", new(2026, 10, 1), new(2026, 10, 5), CancellationToken.None));

        Assert.Equal(("Yahoo", "NOPE123.SA"), (error.Provider, error.Symbol));
    }

    [Theory]
    [InlineData(HttpStatusCode.Unauthorized)]
    [InlineData(HttpStatusCode.Forbidden)]
    public async Task A_refusal_is_a_blocked_request(HttpStatusCode status)
    {
        var handler = new FakeHttpHandler(status, """{"finance":{"result":null,"error":{"code":"Unauthorized","description":"Invalid Crumb"}}}""");

        var error = await Assert.ThrowsAsync<ProviderBlockedException>(
            () => Provider(handler).GetDailyClosesAsync("PETR4.SA", new(2026, 10, 1), new(2026, 10, 5), CancellationToken.None));

        Assert.Equal(("Yahoo", "PETR4.SA"), (error.Provider, error.Symbol));
    }

    [Fact]
    public async Task Too_many_requests_is_rate_limited()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.TooManyRequests, "Too Many Requests") { RetryAfter = TimeSpan.FromSeconds(30) };

        var error = await Assert.ThrowsAsync<ProviderRateLimitedException>(
            () => Provider(handler).GetDailyClosesAsync("PETR4.SA", new(2026, 10, 1), new(2026, 10, 5), CancellationToken.None));

        Assert.Equal(("Yahoo", (TimeSpan?)TimeSpan.FromSeconds(30)), (error.Provider, error.RetryAfter));
    }

    [Theory]
    [InlineData(HttpStatusCode.BadRequest, "yahoo-chart-before-first-trade.json")]
    [InlineData(HttpStatusCode.OK, "yahoo-chart-weekend.json")]
    public async Task A_window_without_a_session_is_an_empty_series(HttpStatusCode status, string fixture)
    {
        var handler = new FakeHttpHandler(status, Fixture(fixture));

        Assert.Empty(await Provider(handler).GetDailyClosesAsync("ITUB4.SA", new(2026, 10, 3), new(2026, 10, 4), CancellationToken.None));
    }

    [Fact]
    public async Task Another_bad_request_is_an_http_error_with_yahoos_description()
    {
        var handler = new FakeHttpHandler(
            HttpStatusCode.BadRequest,
            """{"chart":{"result":null,"error":{"code":"Bad Request","description":"Invalid input - interval=1d is not supported"}}}""");

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => Provider(handler).GetDailyClosesAsync("ITUB4.SA", new(2026, 10, 1), new(2026, 10, 5), CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Contains("interval=1d is not supported", error.Message);
    }

    [Theory]
    [InlineData("")]
    [InlineData("<!DOCTYPE html><html><body>Consent</body></html>")]
    [InlineData("""{"chart":{"result":null,"error":null}}""")]
    [InlineData("""{"chart":{"result":[{"meta":{"gmtoffset":0},"timestamp":[1790812800],"indicators":{"quote":[{"close":["abc"]}]}}]}}""")]
    [InlineData("""{"chart":{"result":[{"meta":{"gmtoffset":0},"timestamp":[1790812800],"indicators":{"quote":[{}]}}]}}""")]
    [InlineData("""{"chart":{"result":[{"meta":{"gmtoffset":0},"timestamp":[1790812800],"events":{"splits":{"1":{"date":1790899200,"numerator":2,"denominator":0}}},"indicators":{"quote":[{"close":[1.5]}]}}]}}""")]
    public async Task A_body_that_is_not_the_chart_is_response_invalid(string body)
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, body);

        var error = await Assert.ThrowsAsync<ProviderResponseInvalidException>(
            () => Provider(handler).GetDailyClosesAsync("BTC-USD", new(2026, 10, 1), new(2026, 10, 5), CancellationToken.None));

        Assert.Equal("Yahoo", error.Provider);
    }

    [Fact]
    public void Yahoo_loads_whole_histories_and_revises_them()
    {
        IPriceProvider provider = Provider(new FakeHttpHandler(HttpStatusCode.OK, "{}"));

        Assert.Equal((ProviderKind.Yahoo, (DateOnly?)new DateOnly(1900, 1, 1), true), (provider.Kind, provider.HistoryStart, provider.RevisesHistory));
    }

    /// <summary>Spec test 8: Yahoo is asked politely, one request start per interval.</summary>
    [Fact]
    public async Task Requests_start_at_least_the_interval_apart()
    {
        var clock = new FakeTimeProvider(Captured);
        var pacer = new YahooPacer(clock);
        var interval = TimeSpan.FromSeconds(1);

        await pacer.WaitTurnAsync(interval, CancellationToken.None);
        var second = pacer.WaitTurnAsync(interval, CancellationToken.None);
        Assert.False(second.IsCompleted);

        clock.Advance(TimeSpan.FromMilliseconds(999));
        Assert.False(second.IsCompleted);

        clock.Advance(TimeSpan.FromMilliseconds(1));
        await second;

        clock.Advance(TimeSpan.FromSeconds(5));
        Assert.True(pacer.WaitTurnAsync(interval, CancellationToken.None).IsCompleted);
    }

    private static YahooProvider Provider(HttpMessageHandler handler, DateTimeOffset? now = null) =>
        new(
            new HttpClient(handler),
            Microsoft.Extensions.Options.Options.Create(Options()),
            new FixedClock(now ?? Captured),
            new YahooPacer(new FixedClock(now ?? Captured)));
}
