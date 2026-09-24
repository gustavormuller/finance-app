using Finance.Api.Application.MarketData;
using Finance.Api.Domain.MarketData;

namespace Finance.Api.Infrastructure.MarketData;

/// <summary>
/// Providers that answer without the network, for the E2E run only (006, spec E2E test
/// 26): <c>MarketData:FakeProviders = true</c> puts them in place of the real adapters.
/// The switch is refused outside Development, at boot
/// (<see cref="MarketDataSetup.RefuseFakeProvidersOutsideDevelopment"/>).
/// </summary>
/// <remarks>
/// Every series gets one value, dated <c>to</c>, so a run writes a row per item and
/// always succeeds. The values are fixed and mean nothing.
/// </remarks>
public static class FakeMarketDataProviders
{
    public static IPriceProviderRegistry Registry { get; } = new PriceProviderRegistry(
        Enum.GetValues<ProviderKind>().Select(kind => (IPriceProvider)new FakePriceProvider(kind)));

    public static IBenchmarkProvider Benchmarks { get; } = new FakeBenchmarkProvider();

    private sealed class FakePriceProvider(ProviderKind kind) : IPriceProvider
    {
        public ProviderKind Kind => kind;

        public Task<IReadOnlyList<DailyClose>> GetDailyClosesAsync(
            string providerSymbol, DateOnly from, DateOnly to, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DailyClose>>([new DailyClose(to, 10m)]);
    }

    private sealed class FakeBenchmarkProvider : IBenchmarkProvider
    {
        public Task<IReadOnlyList<DailyValue>> GetSeriesAsync(
            string code, DateOnly from, DateOnly to, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DailyValue>>([new DailyValue(to, 0.05m)]);
    }
}
