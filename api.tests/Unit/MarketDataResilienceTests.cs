using System.Net;
using System.Text;
using Finance.Api.Infrastructure.MarketData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Polly.CircuitBreaker;
using static Finance.Api.Tests.Unit.MarketDataProviderHarness;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// 006 spec tests 23 and 24: each provider's typed client carries its own retry and
/// circuit breaker (decision 4), driven here through the container's real wiring with a
/// fake primary handler. The retry delay is configured to zero so nothing sleeps.
/// </summary>
public sealed class MarketDataResilienceTests
{
    private static readonly DateOnly From = new(2024, 1, 2);

    private static readonly DateOnly To = new(2024, 1, 8);

    /// <summary>Spec test 23.</summary>
    [Fact]
    public async Task A_transient_503_is_retried_and_the_call_succeeds()
    {
        var brapi = new ScriptedHandler(HttpStatusCode.ServiceUnavailable, HttpStatusCode.OK);
        using var services = Services(brapi, new ScriptedHandler(HttpStatusCode.OK));

        var closes = await services.GetRequiredService<BrapiProvider>()
            .GetDailyClosesAsync("PETR4", From, To, CancellationToken.None);

        Assert.Equal(5, closes.Count);
        Assert.Equal(2, brapi.Calls);
    }

    /// <summary>Spec test 24: five failures reach the provider, then the circuit answers without it.</summary>
    [Fact]
    public async Task Five_consecutive_failures_open_the_circuit_and_the_next_call_fails_fast()
    {
        var brapi = new ScriptedHandler(HttpStatusCode.InternalServerError);
        using var services = Services(brapi, new ScriptedHandler(HttpStatusCode.OK));

        async Task Call() =>
            await services.GetRequiredService<BrapiProvider>().GetDailyClosesAsync("PETR4", From, To, CancellationToken.None);

        // Four attempts in the first call (one and three retries), the fifth opens the circuit.
        await Assert.ThrowsAsync<HttpRequestException>(Call);
        await Assert.ThrowsAsync<BrokenCircuitException>(Call);
        Assert.Equal(5, brapi.Calls);

        await Assert.ThrowsAsync<BrokenCircuitException>(Call);
        Assert.Equal(5, brapi.Calls);
    }

    [Fact]
    public async Task The_circuit_is_per_provider()
    {
        var brapi = new ScriptedHandler(HttpStatusCode.InternalServerError);
        var coinGecko = new ScriptedHandler(HttpStatusCode.OK);
        using var services = Services(brapi, coinGecko);
        for (var call = 0; call < 3; call++)
        {
            await Assert.ThrowsAnyAsync<Exception>(() =>
                services.GetRequiredService<BrapiProvider>().GetDailyClosesAsync("PETR4", From, To, CancellationToken.None));
        }

        await services.GetRequiredService<CoinGeckoProvider>()
            .GetDailyClosesAsync("bitcoin", From, To, CancellationToken.None);

        Assert.Equal(1, coinGecko.Calls);
    }

    /// <summary>A <c>429</c> is the provider asking for less, not a fault: no retry, and it surfaces as-is.</summary>
    [Fact]
    public async Task A_429_is_not_retried()
    {
        var brapi = new ScriptedHandler(HttpStatusCode.TooManyRequests);
        using var services = Services(brapi, new ScriptedHandler(HttpStatusCode.OK));

        await Assert.ThrowsAsync<Finance.Api.Application.MarketData.ProviderRateLimitedException>(() =>
            services.GetRequiredService<BrapiProvider>().GetDailyClosesAsync("PETR4", From, To, CancellationToken.None));

        Assert.Equal(1, brapi.Calls);
    }

    private static ServiceProvider Services(ScriptedHandler brapi, ScriptedHandler coinGecko)
    {
        var configuration = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["MarketData:Brapi:BaseUrl"] = "https://brapi.dev/api/",
            ["MarketData:CoinGecko:BaseUrl"] = "https://api.coingecko.com/api/v3/",
            ["MarketData:CoinGecko:VsCurrency"] = "usd",
            ["MarketData:CoinGecko:MaxHistoryDays"] = "365",
            ["MarketData:Resilience:RetryBaseDelay"] = "00:00:00",
        }).Build();

        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(configuration);
        services.AddLogging();
        services.AddMarketDataProviders();
        brapi.Body = Fixture("brapi-quote-petr4-historical.json");
        coinGecko.Body = Fixture("coingecko-bitcoin-market-chart.json");
        services.AddHttpClient<BrapiProvider>().ConfigurePrimaryHttpMessageHandler(() => brapi);
        services.AddHttpClient<CoinGeckoProvider>().ConfigurePrimaryHttpMessageHandler(() => coinGecko);
        return services.BuildServiceProvider();
    }

    /// <summary>Answers with each status in turn, repeating the last; counts what reached it.</summary>
    private sealed class ScriptedHandler(params HttpStatusCode[] statuses) : HttpMessageHandler
    {
        private int calls;

        public int Calls => calls;

        public string Body { get; set; } = "{}";

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            var status = statuses[Math.Min(Interlocked.Increment(ref calls) - 1, statuses.Length - 1)];
            return Task.FromResult(new HttpResponseMessage(status)
            {
                Content = new StringContent(status == HttpStatusCode.OK ? Body : "{}", Encoding.UTF8, "application/json"),
                RequestMessage = request,
            });
        }
    }
}
