using System.Net;
using System.Text;
using Finance.Api.Application.MarketData;
using Finance.Api.Infrastructure.MarketData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using static Finance.Api.Tests.Unit.MarketDataProviderHarness;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// Spec 025 test 9, through the container's real wiring with a fake primary handler: Yahoo's
/// 429 is a burst to wait out, so it is retried with backoff; brapi's is not (006).
/// </summary>
public sealed class YahooResilienceTests
{
    private static readonly DateOnly From = new(2026, 10, 1);

    private static readonly DateOnly To = new(2026, 10, 5);

    [Fact]
    public async Task A_429_from_yahoo_is_retried_and_the_call_succeeds()
    {
        var yahoo = new ScriptedHandler(Fixture("yahoo-chart-btcusd-today.json"), HttpStatusCode.TooManyRequests, HttpStatusCode.OK);
        using var services = Services(yahoo, new ScriptedHandler(Fixture("brapi-quote-petr4-historical.json"), HttpStatusCode.OK));

        var closes = await services.GetRequiredService<YahooProvider>().GetDailyClosesAsync("BTC-USD", From, To, CancellationToken.None);

        Assert.NotEmpty(closes);
        Assert.Equal(2, yahoo.Calls);
    }

    [Fact]
    public async Task Yahoo_still_refusing_after_the_retries_is_rate_limited()
    {
        var yahoo = new ScriptedHandler("Too Many Requests", HttpStatusCode.TooManyRequests);
        using var services = Services(yahoo, new ScriptedHandler("{}", HttpStatusCode.OK));

        await Assert.ThrowsAsync<ProviderRateLimitedException>(() =>
            services.GetRequiredService<YahooProvider>().GetDailyClosesAsync("BTC-USD", From, To, CancellationToken.None));

        Assert.Equal(4, yahoo.Calls);
    }

    [Fact]
    public async Task Brapis_429_is_still_not_retried()
    {
        var brapi = new ScriptedHandler("{}", HttpStatusCode.TooManyRequests);
        using var services = Services(new ScriptedHandler("{}", HttpStatusCode.OK), brapi);

        await Assert.ThrowsAsync<ProviderRateLimitedException>(() =>
            services.GetRequiredService<BrapiProvider>().GetDailyClosesAsync("PETR4", From, To, CancellationToken.None));

        Assert.Equal(1, brapi.Calls);
    }

    private static ServiceProvider Services(ScriptedHandler yahoo, ScriptedHandler brapi)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MarketData:Yahoo:BaseUrl"] = "https://query1.finance.yahoo.com/v8/finance/chart/",
            ["MarketData:Yahoo:UserAgent"] = "Mozilla/5.0",
            ["MarketData:Yahoo:RequestInterval"] = "00:00:00",
            ["MarketData:Brapi:BaseUrl"] = "https://brapi.dev/api/",
            ["MarketData:Brapi:Token"] = "brapi-test-token",
            ["MarketData:Resilience:RetryBaseDelay"] = "00:00:00",
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddMarketDataProviders();
        services.AddHttpClient<YahooProvider>().ConfigurePrimaryHttpMessageHandler(() => yahoo);
        services.AddHttpClient<BrapiProvider>().ConfigurePrimaryHttpMessageHandler(() => brapi);
        return services.BuildServiceProvider();
    }

    /// <summary>Answers with each status in turn, repeating the last; counts what reached it.</summary>
    private sealed class ScriptedHandler(string body, params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        private int calls;

        public int Calls => calls;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var status = statuses[Math.Min(Interlocked.Increment(ref calls) - 1, statuses.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(status == HttpStatusCode.OK ? body : "Too Many Requests", Encoding.UTF8, "application/json"),
                RequestMessage = request,
            });
        }
    }
}
