using Finance.Api.Application.MarketData;
using Microsoft.Extensions.DependencyInjection.Extensions;

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

        services.AddHttpClient<BcbSgsProvider>();
        services.AddTransient<IBenchmarkProvider>(provider => provider.GetRequiredService<BcbSgsProvider>());

        return services;
    }

    // Transient, like the typed clients: a provider never outlives the handler rotation
    // of the factory. Per-provider resilience (006, decision 4) attaches to the returned
    // builder.
    private static IHttpClientBuilder AddPriceProvider<TProvider>(this IServiceCollection services)
        where TProvider : class, IPriceProvider
    {
        var builder = services.AddHttpClient<TProvider>();
        services.AddTransient<IPriceProvider>(provider => provider.GetRequiredService<TProvider>());
        return builder;
    }
}
