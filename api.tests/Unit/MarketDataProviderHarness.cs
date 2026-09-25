using System.Net;
using System.Text;
using Finance.Api.Application.MarketData;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// What the 006 provider tests share: a handler that answers from a fixture and records
/// what was asked (decision 9, the network is never hit), a fixed clock, and options
/// pointing at the real base URLs with fake keys.
/// </summary>
internal static class MarketDataProviderHarness
{
    public static string Fixture(string name) =>
        File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Fixtures", "MarketData", name));

    public static MarketDataOptions Options() => new()
    {
        Schedule = "0 3 * * *",
        BackfillYears = 5,
        Bcb = new BcbOptions
        {
            BaseUrl = "https://api.bcb.gov.br/dados/serie/bcdata.sgs.",
            Series = new(StringComparer.OrdinalIgnoreCase)
            {
                ["CDI"] = new SgsSeries { Code = 12, Unit = BenchmarkUnit.PercentPerDay },
                ["IPCA"] = new SgsSeries { Code = 433, Unit = BenchmarkUnit.PercentPerMonth },
            },
        },
        Brapi = new BrapiOptions { BaseUrl = "https://brapi.dev/api/", Token = "brapi-test-token" },
        CoinGecko = new CoinGeckoOptions
        {
            BaseUrl = "https://api.coingecko.com/api/v3/",
            DemoKey = "coingecko-test-key",
            VsCurrency = "usd",
            MaxHistoryDays = 365,
        },
        TwelveData = new TwelveDataOptions { BaseUrl = "https://api.twelvedata.com/", Key = "twelvedata-test-key" },
        Binance = new BinanceOptions { BaseUrl = "https://api.binance.com/api/v3/" },
    };
}

/// <summary>Answers every request with one status and body, and keeps the requests.</summary>
internal sealed class FakeHttpHandler(HttpStatusCode status, string body) : HttpMessageHandler
{
    public List<HttpRequestMessage> Requests { get; } = [];

    public TimeSpan? RetryAfter { get; init; }

    public HttpClient Client() => new(this);

    public HttpRequestMessage Single() => Assert.Single(Requests);

    protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
    {
        Requests.Add(request);
        var response = new HttpResponseMessage(status)
        {
            Content = new StringContent(body, Encoding.UTF8, "application/json"),
            RequestMessage = request,
        };
        if (RetryAfter is { } delay)
        {
            response.Headers.RetryAfter = new System.Net.Http.Headers.RetryConditionHeaderValue(delay);
        }

        return Task.FromResult(response);
    }
}

/// <summary>A clock stopped at one instant.</summary>
internal sealed class FixedClock(DateTimeOffset now) : TimeProvider
{
    public override DateTimeOffset GetUtcNow() => now;
}
