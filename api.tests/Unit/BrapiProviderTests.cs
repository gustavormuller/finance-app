using System.Net;
using Finance.Api.Application.MarketData;
using Finance.Api.Infrastructure.MarketData;
using static Finance.Api.Tests.Unit.MarketDataProviderHarness;

namespace Finance.Api.Tests.Unit;

/// <summary>006 spec tests 3, 4, 7 and 8 for brapi, against a fixture, never the network.</summary>
public sealed class BrapiProviderTests
{
    private static readonly DateTimeOffset Now = new(2024, 1, 9, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task Historical_range_is_parsed()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, Fixture("brapi-quote-petr4-historical.json"));

        var closes = await Provider(handler).GetDailyClosesAsync(
            "PETR4", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 8), CancellationToken.None);

        Assert.Equal(
            [new(new(2024, 1, 2), 37.64m), new(new(2024, 1, 3), 38.18m), new(new(2024, 1, 4), 37.93m),
             new(new(2024, 1, 5), 37.62m), new DailyClose(new(2024, 1, 8), 37.95m)],
            closes);

        var request = handler.Single();
        Assert.Equal("https://brapi.dev/api/quote/PETR4?range=1mo&interval=1d", request.RequestUri!.ToString());
        Assert.Equal("Bearer", request.Headers.Authorization!.Scheme);
        Assert.Equal("brapi-test-token", request.Headers.Authorization.Parameter);
    }

    [Theory]
    [InlineData("2024-01-05", "5d")]
    [InlineData("2023-12-09", "1mo")]
    [InlineData("2023-12-08", "3mo")]
    [InlineData("2023-10-09", "3mo")]
    [InlineData("2023-07-09", "6mo")]
    [InlineData("2023-01-09", "1y")]
    [InlineData("2022-01-09", "2y")]
    [InlineData("2019-01-09", "5y")]
    [InlineData("2014-01-09", "10y")]
    [InlineData("2013-12-31", "max")]
    public async Task The_smallest_range_that_reaches_from_is_requested(string from, string range)
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, Fixture("brapi-quote-petr4-historical.json"));

        await Provider(handler).GetDailyClosesAsync(
            "PETR4", DateOnly.Parse(from, System.Globalization.CultureInfo.InvariantCulture), new DateOnly(2024, 1, 9), CancellationToken.None);

        Assert.Equal($"?range={range}&interval=1d", handler.Single().RequestUri!.Query);
    }

    [Fact]
    public async Task Timestamps_are_read_as_days_in_Sao_Paulo()
    {
        // 03:00 UTC on 3 January is midnight in Sao Paulo; 02:00 UTC is still 2 January there.
        var handler = new FakeHttpHandler(HttpStatusCode.OK, """
            {"results":[{"symbol":"PETR4","historicalDataPrice":[
              {"date":1704247200,"close":37.64},{"date":1704250800,"close":38.18}]}]}
            """);

        var closes = await Provider(handler).GetDailyClosesAsync(
            "PETR4", new DateOnly(2024, 1, 1), new DateOnly(2024, 1, 9), CancellationToken.None);

        Assert.Equal([new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 3)], closes.Select(close => close.Date));
    }

    [Fact]
    public async Task Unknown_ticker_is_empty_not_an_exception()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.NotFound, Fixture("brapi-quote-unknown-ticker.json"));

        Assert.Empty(await Provider(handler).GetDailyClosesAsync(
            "XXXX1", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 8), CancellationToken.None));
    }

    [Theory]
    [InlineData("""{"results":[]}""")]
    [InlineData("""{"results":[{"symbol":"PETR4"}]}""")]
    [InlineData("""{"results":[{"symbol":"PETR4","historicalDataPrice":[{"date":1704204000,"close":null}]}]}""")]
    public async Task No_history_in_the_body_is_empty(string body)
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, body);

        Assert.Empty(await Provider(handler).GetDailyClosesAsync(
            "PETR4", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 8), CancellationToken.None));
    }

    [Fact]
    public async Task Without_a_token_no_authorization_header_is_sent()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, Fixture("brapi-quote-petr4-historical.json"));
        var options = Options();
        options.Brapi.Token = "";

        await new BrapiProvider(handler.Client(), Microsoft.Extensions.Options.Options.Create(options), new FixedClock(Now))
            .GetDailyClosesAsync("PETR4", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 8), CancellationToken.None);

        Assert.Null(handler.Single().Headers.Authorization);
    }

    [Fact]
    public async Task Too_many_requests_is_rate_limited_not_a_crash()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.TooManyRequests, """{"error":true,"message":"rate limit"}""");

        var error = await Assert.ThrowsAsync<ProviderRateLimitedException>(() => Provider(handler).GetDailyClosesAsync(
            "PETR4", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 8), CancellationToken.None));

        Assert.Equal("Brapi", error.Provider);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"results\":[")]
    [InlineData("""{"results":{}}""")]
    [InlineData("""{"error":true}""")]
    [InlineData("""{"results":[{"historicalDataPrice":[{"date":"2024-01-02","close":37.64}]}]}""")]
    [InlineData("""{"results":[{"historicalDataPrice":[{"date":1704204000,"close":"abc"}]}]}""")]
    [InlineData("""{"results":[{"historicalDataPrice":[{"close":37.64}]}]}""")]
    public async Task Malformed_json_is_response_invalid_and_nothing_else_escapes(string body)
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, body);

        var error = await Assert.ThrowsAsync<ProviderResponseInvalidException>(() => Provider(handler).GetDailyClosesAsync(
            "PETR4", new DateOnly(2024, 1, 2), new DateOnly(2024, 1, 8), CancellationToken.None));

        Assert.Equal("Brapi", error.Provider);
    }

    private static BrapiProvider Provider(FakeHttpHandler handler) =>
        new(handler.Client(), Microsoft.Extensions.Options.Options.Create(Options()), new FixedClock(Now));
}
