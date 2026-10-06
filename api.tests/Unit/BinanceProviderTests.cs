using System.Globalization;
using System.Net;
using System.Text;
using Finance.Api.Application.MarketData;
using Finance.Api.Infrastructure.MarketData;
using static Finance.Api.Tests.Unit.MarketDataProviderHarness;

namespace Finance.Api.Tests.Unit;

/// <summary>019 test plan 1 to 10: Binance's daily klines, against captured responses, never the network.</summary>
public sealed class BinanceProviderTests
{
    private static readonly DateOnly From = new(2026, 9, 20);
    private static readonly DateOnly To = new(2026, 9, 24);

    [Fact]
    public async Task Klines_are_parsed_to_utc_days_and_decimal_closes()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, Fixture("binance-klines-btcbrl.json"));

        var closes = await Provider(handler).GetDailyClosesAsync("BTCBRL", From, To, CancellationToken.None);

        Assert.Equal(
            [new(new(2026, 9, 20), 418772m), new(new(2026, 9, 21), 443999m), new(new(2026, 9, 22), 441549m),
             new(new(2026, 9, 23), 437110m), new DailyClose(new(2026, 9, 24), 439453m)],
            closes);
        Assert.Equal(
            "https://api.binance.com/api/v3/klines?symbol=BTCBRL&interval=1d"
            + "&startTime=1789862400000&endTime=1790294399999&limit=1000",
            handler.Single().RequestUri!.ToString());
    }

    [Fact]
    public async Task A_candle_is_dated_by_the_utc_day_it_opens()
    {
        // 23:59:59.999 UTC on the 20th opens nothing real, but it must still be the 20th.
        var handler = new FakeHttpHandler(HttpStatusCode.OK, """[[1789948799999,"0","0","0","1"],[1789948800000,"0","0","0","2"]]""");

        var closes = await Provider(handler).GetDailyClosesAsync("BTCBRL", From, To, CancellationToken.None);

        Assert.Equal([new(new(2026, 9, 20), 1m), new DailyClose(new(2026, 9, 21), 2m)], closes);
    }

    [Fact]
    public async Task Closes_reach_decimal_without_passing_through_double()
    {
        // 26 significant digits: a double keeps about 17 and would round this.
        var handler = new FakeHttpHandler(HttpStatusCode.OK, """[[1789862400000,"0","0","0","12345678.123456789012345678"]]""");

        var close = Assert.Single(await Provider(handler).GetDailyClosesAsync("BTCBRL", From, To, CancellationToken.None));

        Assert.Equal(12345678.123456789012345678m, close.Close);
    }

    [Fact]
    public async Task A_long_range_is_fetched_in_pages_of_a_thousand_candles()
    {
        var from = new DateOnly(2021, 9, 25);
        var handler = new KlinesHandler(from, To);

        var closes = await Provider(handler).GetDailyClosesAsync("BTCBRL", from, To, CancellationToken.None);

        Assert.Equal(To.DayNumber - from.DayNumber + 1, closes.Count);
        Assert.Equal((from, To), (closes[0].Date, closes[^1].Date));
        Assert.Equal(
            [Milliseconds(from), Milliseconds(from.AddDays(999)) + 1],
            handler.StartTimes);
    }

    [Fact]
    public async Task A_page_that_does_not_move_forward_ends_the_paging()
    {
        // A full page that ignores startTime would otherwise be asked for forever.
        var body = new StringBuilder("[");
        for (var i = 0; i < BinanceProvider.PageSize; i++)
        {
            body.Append(i == 0 ? "" : ",").Append(CultureInfo.InvariantCulture, $"""[{Milliseconds(From) - 1},"0","0","0","1"]""");
        }

        var handler = new FakeHttpHandler(HttpStatusCode.OK, body.Append(']').ToString());

        Assert.Empty(await Provider(handler).GetDailyClosesAsync("BTCBRL", From, To, CancellationToken.None));
        Assert.Single(handler.Requests);
    }

    [Fact]
    public async Task A_lowercase_symbol_is_sent_upper_cased()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "[]");

        await Provider(handler).GetDailyClosesAsync(" btcbrl ", From, To, CancellationToken.None);

        Assert.StartsWith("?symbol=BTCBRL&", handler.Single().RequestUri!.Query);
    }

    [Fact]
    public async Task Unknown_symbol_is_empty_not_an_exception()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.BadRequest, Fixture("binance-klines-invalid-symbol.json"));

        Assert.Empty(await Provider(handler).GetDailyClosesAsync("NOPEBRL", From, To, CancellationToken.None));
    }

    [Fact]
    public async Task Another_bad_request_is_an_http_error_with_its_status()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.BadRequest, """{"code":-1100,"msg":"Illegal characters found in parameter 'symbol'."}""");

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => Provider(handler).GetDailyClosesAsync("BTC/BRL", From, To, CancellationToken.None));

        Assert.Equal(HttpStatusCode.BadRequest, error.StatusCode);
        Assert.Contains("-1100", error.Message);
    }

    [Theory]
    [InlineData(429)]
    [InlineData(418)]
    public async Task Too_many_requests_or_a_ban_is_rate_limited_not_a_crash(int status)
    {
        var handler = new FakeHttpHandler((HttpStatusCode)status, """{"code":-1003,"msg":"Too many requests."}""")
        {
            RetryAfter = TimeSpan.FromSeconds(60),
        };

        var error = await Assert.ThrowsAsync<ProviderRateLimitedException>(
            () => Provider(handler).GetDailyClosesAsync("BTCBRL", From, To, CancellationToken.None));

        Assert.Equal("Binance", error.Provider);
        Assert.Equal(TimeSpan.FromSeconds(60), error.RetryAfter);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[[1789862400000,")]
    [InlineData("{}")]
    [InlineData("[[1789862400000]]")]
    [InlineData("""[["2026-09-20","0","0","0","1"]]""")]
    [InlineData("""[[1789862400000,"0","0","0","abc"]]""")]
    [InlineData("""[[1789862400000,"0","0","0",null]]""")]
    public async Task Malformed_json_is_response_invalid_and_nothing_else_escapes(string body)
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, body);

        var error = await Assert.ThrowsAsync<ProviderResponseInvalidException>(
            () => Provider(handler).GetDailyClosesAsync("BTCBRL", From, To, CancellationToken.None));

        Assert.Equal("Binance", error.Provider);
    }

    private static long Milliseconds(DateOnly day) =>
        new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeMilliseconds();

    private static BinanceProvider Provider(HttpMessageHandler handler) =>
        new(new HttpClient(handler), Microsoft.Extensions.Options.Options.Create(Options()));

    /// <summary>
    /// Binance's klines for one candle a day in <c>[first, last]</c>: each request gets at
    /// most <see cref="BinanceProvider.PageSize"/> candles from its <c>startTime</c> on.
    /// </summary>
    private sealed class KlinesHandler(DateOnly first, DateOnly last) : HttpMessageHandler
    {
        public List<long> StartTimes { get; } = [];

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var query = System.Web.HttpUtility.ParseQueryString(request.RequestUri!.Query);
            var start = long.Parse(query["startTime"]!, CultureInfo.InvariantCulture);
            StartTimes.Add(start);

            var candles = Enumerable.Range(0, last.DayNumber - first.DayNumber + 1)
                .Select(i => Milliseconds(first.AddDays(i)))
                .Where(open => open >= start)
                .Take(BinanceProvider.PageSize)
                .Select(open => $"""[{open},"0","0","0","1.5"]""");

            return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent($"[{string.Join(',', candles)}]", Encoding.UTF8, "application/json"),
                RequestMessage = request,
            });
        }
    }
}
