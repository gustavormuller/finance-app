using System.Globalization;
using System.Net;
using Finance.Api.Application.MarketData;
using Finance.Api.Infrastructure.MarketData;
using static Finance.Api.Tests.Unit.MarketDataProviderHarness;

namespace Finance.Api.Tests.Unit;

/// <summary>006 spec tests 1, 2, 7 and 8 for BCB SGS, against a fixture, never the network.</summary>
public sealed class BcbSgsProviderTests
{
    private static readonly DateOnly From = new(2024, 1, 2);
    private static readonly DateOnly To = new(2024, 1, 15);

    [Fact]
    public async Task Happy_path_parses_dates_and_values()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, Fixture("bcb-sgs-12-cdi.json"));

        var values = await Provider(handler).GetSeriesAsync("CDI", From, To, CancellationToken.None);

        Assert.Equal(10, values.Count);
        Assert.Equal(new DailyValue(new DateOnly(2024, 1, 2), 0.043739m), values[0]);
        Assert.Equal(new DailyValue(new DateOnly(2024, 1, 15), 0.043739m), values[^1]);
        Assert.Equal(
            "https://api.bcb.gov.br/dados/serie/bcdata.sgs.12/dados?formato=json&dataInicial=02/01/2024&dataFinal=15/01/2024",
            handler.Single().RequestUri!.ToString());
    }

    [Theory]
    [InlineData("MM/dd/yyyy", ".")]
    [InlineData("dd/MM/yyyy", ",")]
    [InlineData("yyyy-MM-dd", ",")]
    public async Task Dates_are_dd_MM_yyyy_and_values_invariant_whatever_the_current_culture(
        string shortDatePattern, string decimalSeparator)
    {
        var previous = CultureInfo.CurrentCulture;
        CultureInfo.CurrentCulture = Hostile(shortDatePattern, decimalSeparator);
        try
        {
            var handler = new FakeHttpHandler(HttpStatusCode.OK, Fixture("bcb-sgs-12-cdi.json"));

            var values = await Provider(handler).GetSeriesAsync("CDI", From, To, CancellationToken.None);

            // 02/01/2024 is 2 January; read as MM/dd it would be 1 February, and
            // 15/01/2024 would not parse at all.
            Assert.Equal(
                [new(2024, 1, 2), new(2024, 1, 3), new(2024, 1, 4), new(2024, 1, 5), new(2024, 1, 8),
                 new(2024, 1, 9), new(2024, 1, 10), new(2024, 1, 11), new(2024, 1, 12), new DateOnly(2024, 1, 15)],
                values.Select(value => value.Date));
            Assert.All(values, value => Assert.Equal(0.043739m, value.Value));
            Assert.Contains("dataInicial=02/01/2024&dataFinal=15/01/2024", handler.Single().RequestUri!.Query);
        }
        finally
        {
            CultureInfo.CurrentCulture = previous;
        }
    }

    [Fact]
    public async Task Days_outside_the_requested_range_are_dropped()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, Fixture("bcb-sgs-12-cdi.json"));

        var values = await Provider(handler).GetSeriesAsync(
            "CDI", new DateOnly(2024, 1, 5), new DateOnly(2024, 1, 9), CancellationToken.None);

        Assert.Equal([new(2024, 1, 5), new(2024, 1, 8), new DateOnly(2024, 1, 9)], values.Select(value => value.Date));
    }

    [Fact]
    public async Task Not_found_is_an_empty_series()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.NotFound, """{"error":"Value(s) not found"}""");

        Assert.Empty(await Provider(handler).GetSeriesAsync("CDI", From, To, CancellationToken.None));
    }

    [Fact]
    public async Task A_code_with_no_configured_series_is_refused_without_a_request()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "[]");

        await Assert.ThrowsAsync<InvalidOperationException>(
            () => Provider(handler).GetSeriesAsync("NOPE", From, To, CancellationToken.None));
        Assert.Empty(handler.Requests);
    }

    [Fact]
    public async Task Too_many_requests_is_rate_limited_not_a_crash()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.TooManyRequests, "")
        {
            RetryAfter = TimeSpan.FromSeconds(30),
        };

        var error = await Assert.ThrowsAsync<ProviderRateLimitedException>(
            () => Provider(handler).GetSeriesAsync("CDI", From, To, CancellationToken.None));

        Assert.Equal("BcbSgs", error.Provider);
        Assert.Equal(TimeSpan.FromSeconds(30), error.RetryAfter);
    }

    [Theory]
    [InlineData("")]
    [InlineData("[{\"data\":\"02/01/2024\",")]
    [InlineData("<html>Service Unavailable</html>")]
    [InlineData("""{"data":"02/01/2024","valor":"0.043739"}""")]
    [InlineData("""[{"data":"2024-01-02","valor":"0.043739"}]""")]
    [InlineData("""[{"data":"02/01/2024","valor":"n/a"}]""")]
    [InlineData("""[{"data":"02/01/2024"}]""")]
    [InlineData("""[{"data":"02/01/2024","valor":true}]""")]
    public async Task Malformed_json_is_response_invalid_and_nothing_else_escapes(string body)
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, body);

        var error = await Assert.ThrowsAsync<ProviderResponseInvalidException>(
            () => Provider(handler).GetSeriesAsync("CDI", From, To, CancellationToken.None));

        Assert.Equal("BcbSgs", error.Provider);
    }

    /// <summary>
    /// A current culture that misreads BCB's text if the parser consulted it: month-first
    /// dates like en-US, a decimal comma like pt-BR. Built from the invariant culture
    /// because test hosts may run in globalization-invariant mode, with no named cultures.
    /// </summary>
    private static CultureInfo Hostile(string shortDatePattern, string decimalSeparator)
    {
        var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
        culture.DateTimeFormat.ShortDatePattern = shortDatePattern;
        culture.NumberFormat.NumberDecimalSeparator = decimalSeparator;
        culture.NumberFormat.NumberGroupSeparator = decimalSeparator == "," ? "." : ",";
        return culture;
    }

    private static BcbSgsProvider Provider(FakeHttpHandler handler) =>
        new(handler.Client(), Microsoft.Extensions.Options.Options.Create(Options()));
}
