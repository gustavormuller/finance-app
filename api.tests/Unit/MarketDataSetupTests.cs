using Finance.Api.Application.MarketData;
using Finance.Api.Domain.MarketData;
using Finance.Api.Infrastructure.MarketData;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.Api.Tests.Unit;

/// <summary>006: the container hands out one typed-client adapter per port and kind.</summary>
public sealed class MarketDataSetupTests
{
    [Fact]
    public void Registry_resolves_every_provider_kind_to_its_adapter()
    {
        using var services = Services();
        var registry = services.GetRequiredService<IPriceProviderRegistry>();

        Assert.IsType<BrapiProvider>(registry.For(ProviderKind.Brapi));
        Assert.IsType<CoinGeckoProvider>(registry.For(ProviderKind.CoinGecko));
        Assert.IsType<TwelveDataProvider>(registry.For(ProviderKind.TwelveData));
        Assert.Equal(
            Enum.GetValues<ProviderKind>().Order(),
            services.GetServices<IPriceProvider>().Select(provider => provider.Kind).Order());
    }

    [Fact]
    public void The_benchmark_port_is_bcb_sgs()
    {
        using var services = Services();

        Assert.IsType<BcbSgsProvider>(services.GetRequiredService<IBenchmarkProvider>());
    }

    private static ServiceProvider Services()
    {
        var services = new ServiceCollection();
        services.AddSingleton<IConfiguration>(new ConfigurationBuilder().Build());
        services.AddMarketDataProviders();
        return services.BuildServiceProvider(new ServiceProviderOptions { ValidateOnBuild = true, ValidateScopes = true });
    }
}
