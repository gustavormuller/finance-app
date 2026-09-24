using Finance.Api.Application.MarketData;
using Finance.Api.Domain.MarketData;

namespace Finance.Api.Tests.Unit;

/// <summary>006 provider registry: one implementation per <see cref="ProviderKind"/>.</summary>
public sealed class PriceProviderRegistryTests
{
    [Fact]
    public void For_returns_the_provider_registered_for_each_kind()
    {
        var brapi = new StubProvider(ProviderKind.Brapi);
        var coinGecko = new StubProvider(ProviderKind.CoinGecko);
        var twelveData = new StubProvider(ProviderKind.TwelveData);

        var registry = new PriceProviderRegistry([brapi, coinGecko, twelveData]);

        Assert.Same(brapi, registry.For(ProviderKind.Brapi));
        Assert.Same(coinGecko, registry.For(ProviderKind.CoinGecko));
        Assert.Same(twelveData, registry.For(ProviderKind.TwelveData));
    }

    [Fact]
    public void For_an_unregistered_kind_throws_naming_it()
    {
        var registry = new PriceProviderRegistry([new StubProvider(ProviderKind.Brapi)]);

        var error = Assert.Throws<InvalidOperationException>(() => registry.For(ProviderKind.TwelveData));

        Assert.Contains(nameof(ProviderKind.TwelveData), error.Message);
    }

    [Fact]
    public void Two_providers_for_one_kind_are_refused()
    {
        var error = Assert.Throws<InvalidOperationException>(() => new PriceProviderRegistry(
            [new StubProvider(ProviderKind.CoinGecko), new StubProvider(ProviderKind.CoinGecko)]));

        Assert.Contains(nameof(ProviderKind.CoinGecko), error.Message);
    }

    private sealed class StubProvider(ProviderKind kind) : IPriceProvider
    {
        public ProviderKind Kind { get; } = kind;

        public Task<IReadOnlyList<DailyClose>> GetDailyClosesAsync(
            string providerSymbol, DateOnly from, DateOnly to, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DailyClose>>([]);
    }
}
