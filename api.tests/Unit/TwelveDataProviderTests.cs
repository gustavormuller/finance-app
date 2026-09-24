using System.Net;
using Finance.Api.Application.MarketData;
using Finance.Api.Infrastructure.MarketData;
using static Finance.Api.Tests.Unit.MarketDataProviderHarness;

namespace Finance.Api.Tests.Unit;

/// <summary>006 spec tests 6, 7 and 8 for Twelve Data, against a fixture, never the network.</summary>
public sealed class TwelveDataProviderTests
{
    private static readonly DateOnly From = new(2024, 1, 2);
    private static readonly DateOnly To = new(2024, 1, 8);

    [Fact]
    public async Task Time_series_is_parsed_oldest_first()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, Fixture("twelvedata-time-series-aapl.json"));

        var closes = await Provider(handler).GetDailyClosesAsync("AAPL", From, To, CancellationToken.None);

        Assert.Equal(
            [new(new(2024, 1, 2), 185.64m), new(new(2024, 1, 3), 184.25m), new(new(2024, 1, 4), 181.91m),
             new(new(2024, 1, 5), 181.17999m), new DailyClose(new(2024, 1, 8), 185.56m)],
            closes);

        var request = handler.Single();
        Assert.Equal(
            "https://api.twelvedata.com/time_series?symbol=AAPL&interval=1day&start_date=2024-01-02&end_date=2024-01-09&outputsize=5000",
            request.RequestUri!.ToString());
        Assert.Equal("apikey", request.Headers.Authorization!.Scheme);
        Assert.Equal("twelvedata-test-key", request.Headers.Authorization.Parameter);
    }

    [Theory]
    [InlineData(HttpStatusCode.TooManyRequests)]
    [InlineData(HttpStatusCode.OK)]
    public async Task Too_many_requests_is_rate_limited_whether_in_the_status_or_the_body(HttpStatusCode status)
    {
        var handler = new FakeHttpHandler(
            status, """{"code":429,"message":"You have run out of API credits for the current minute.","status":"error"}""");

        var error = await Assert.ThrowsAsync<ProviderRateLimitedException>(
            () => Provider(handler).GetDailyClosesAsync("AAPL", From, To, CancellationToken.None));

        Assert.Equal("TwelveData", error.Provider);
    }

    [Theory]
    [InlineData(HttpStatusCode.OK, """{"code":404,"message":"**symbol** not found: XXXX. Please specify it correctly.","status":"error"}""")]
    [InlineData(HttpStatusCode.OK, """{"code":400,"message":"No data is available on the specified dates. Try setting different start/end dates.","status":"error"}""")]
    [InlineData(HttpStatusCode.BadRequest, """{"code":400,"message":"No data is available on the specified dates. Try setting different start/end dates.","status":"error"}""")]
    public async Task Unknown_symbol_or_no_data_in_the_range_is_empty(HttpStatusCode status, string body)
    {
        var handler = new FakeHttpHandler(status, body);

        Assert.Empty(await Provider(handler).GetDailyClosesAsync("XXXX", From, To, CancellationToken.None));
    }

    [Fact]
    public async Task Any_other_api_error_is_an_http_error_with_its_code()
    {
        var handler = new FakeHttpHandler(
            HttpStatusCode.OK, """{"code":401,"message":"**apikey** parameter is incorrect or not specified.","status":"error"}""");

        var error = await Assert.ThrowsAsync<HttpRequestException>(
            () => Provider(handler).GetDailyClosesAsync("AAPL", From, To, CancellationToken.None));

        Assert.Equal(HttpStatusCode.Unauthorized, error.StatusCode);
    }

    [Theory]
    [InlineData("")]
    [InlineData("{\"values\":[{\"datetime\":")]
    [InlineData("""{"status":"ok"}""")]
    [InlineData("""{"values":{},"status":"ok"}""")]
    [InlineData("""{"values":[{"datetime":"01/02/2024","close":"185.64000"}],"status":"ok"}""")]
    [InlineData("""{"values":[{"datetime":"2024-01-02","close":"n/a"}],"status":"ok"}""")]
    [InlineData("""{"values":[{"datetime":"2024-01-02"}],"status":"ok"}""")]
    public async Task Malformed_json_is_response_invalid_and_nothing_else_escapes(string body)
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, body);

        var error = await Assert.ThrowsAsync<ProviderResponseInvalidException>(
            () => Provider(handler).GetDailyClosesAsync("AAPL", From, To, CancellationToken.None));

        Assert.Equal("TwelveData", error.Provider);
    }

    private static TwelveDataProvider Provider(FakeHttpHandler handler) =>
        new(handler.Client(), Microsoft.Extensions.Options.Options.Create(Options()));
}
