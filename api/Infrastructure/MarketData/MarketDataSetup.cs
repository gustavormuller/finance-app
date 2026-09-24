using System.Net;
using Finance.Api.Application.MarketData;
using Finance.Api.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Http.Resilience;
using Microsoft.Extensions.Options;
using Polly;

namespace Finance.Api.Infrastructure.MarketData;

/// <summary>
/// 006's provider adapters, each a typed <see cref="HttpClient"/> from the factory, and
/// the <c>MarketData</c> settings they read. Adding a price provider is one class and
/// one <see cref="AddPriceProvider{TProvider}"/> line.
/// </summary>
public static class MarketDataSetup
{
    public static IServiceCollection AddMarketDataProviders(this IServiceCollection services)
    {
        services.AddOptions<MarketDataOptions>().BindConfiguration(MarketDataOptions.Section);
        services.TryAddSingleton(TimeProvider.System);

        services.AddPriceProvider<BrapiProvider>();
        services.AddPriceProvider<CoinGeckoProvider>();
        services.AddPriceProvider<TwelveDataProvider>();
        services.AddTransient<IPriceProviderRegistry, PriceProviderRegistry>();

        services.AddHttpClient<BcbSgsProvider>().WithResilience();
        services.AddTransient<IBenchmarkProvider>(provider => provider.GetRequiredService<BcbSgsProvider>());

        return services;
    }

    /// <summary>
    /// The sync and its nightly host. Scoped, like the context they take (ADR-016); the job
    /// opens a scope per run. <c>MarketData:ScheduledSync</c> decides whether the job runs.
    /// </summary>
    public static IServiceCollection AddMarketDataSync(this IServiceCollection services)
    {
        services.AddScoped<MarketDataStore>();
        services.AddScoped<MarketDataSync>();
        services.AddHostedService<MarketDataSyncJob>();
        return services;
    }

    // Transient, like the typed clients: a provider never outlives the handler rotation
    // of the factory.
    private static IHttpClientBuilder AddPriceProvider<TProvider>(this IServiceCollection services)
        where TProvider : class, IPriceProvider
    {
        var builder = services.AddHttpClient<TProvider>().WithResilience();
        services.AddTransient<IPriceProvider>(provider => provider.GetRequiredService<TProvider>());
        return builder;
    }

    /// <summary>
    /// Per-provider resilience (006, decision 4): a total timeout, then retry with
    /// exponential backoff, then a circuit breaker, then a per-attempt timeout. The
    /// pipeline is keyed by the typed client's name, so each provider has its own circuit,
    /// and it lives in a singleton registry, so the circuit outlives each client.
    /// </summary>
    /// <remarks>
    /// A 429 is neither retried nor counted against the circuit: the adapter turns it into
    /// <see cref="ProviderRateLimitedException"/> and the sync stops asking that provider
    /// for the rest of the run. Five failures open the circuit: every attempt in the
    /// sampling window failed, and at least five were made.
    /// </remarks>
    private static IHttpClientBuilder WithResilience(this IHttpClientBuilder builder)
    {
        builder.ConfigureHttpClient(client => client.Timeout = Timeout.InfiniteTimeSpan);
        builder.AddResilienceHandler("market-data", (pipeline, context) =>
        {
            var settings = context.ServiceProvider.GetRequiredService<IOptions<MarketDataOptions>>().Value.Resilience;
            pipeline
                .AddTimeout(settings.TotalTimeout)
                .AddRetry(new HttpRetryStrategyOptions
                {
                    MaxRetryAttempts = settings.RetryAttempts,
                    Delay = settings.RetryBaseDelay,
                    BackoffType = DelayBackoffType.Exponential,
                    UseJitter = true,
                    ShouldHandle = args => ValueTask.FromResult(IsTransient(args.Outcome)),
                })
                .AddCircuitBreaker(new HttpCircuitBreakerStrategyOptions
                {
                    FailureRatio = 1.0,
                    MinimumThroughput = settings.FailuresToBreak,
                    SamplingDuration = settings.SamplingDuration,
                    BreakDuration = settings.BreakDuration,
                    ShouldHandle = args => ValueTask.FromResult(IsTransient(args.Outcome)),
                })
                .AddTimeout(settings.AttemptTimeout);
        });
        return builder;
    }

    private static bool IsTransient(Outcome<HttpResponseMessage> outcome) =>
        outcome.Result?.StatusCode != HttpStatusCode.TooManyRequests
        && HttpClientResiliencePredicates.IsTransient(outcome);
}
