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
/// A price series gets a close for every calendar day in <c>[from, to]</c>: 10 on
/// <c>to</c>, one cent lower for each day before it, and 5 from 500 days back. So the
/// latest close is always 10 (007's E2E reads it as the value), and a buy dated some days
/// back has a real return (008's E2E test 36). A close depends on the run that fetched it,
/// since it is counted back from that run's <c>to</c>; the sync never refetches a stored
/// day. A benchmark series gets one value, 0.05, dated <c>to</c>. None of it means anything.
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
            Task.FromResult<IReadOnlyList<DailyClose>>(
                Enumerable.Range(0, Math.Max(0, to.DayNumber - from.DayNumber + 1))
                    .Select(i => from.AddDays(i))
                    .Select(date => new DailyClose(date, 10m - 0.01m * Math.Min(to.DayNumber - date.DayNumber, 500)))
                    .ToList());
    }

    private sealed class FakeBenchmarkProvider : IBenchmarkProvider
    {
        public Task<IReadOnlyList<DailyValue>> GetSeriesAsync(
            string code, DateOnly from, DateOnly to, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DailyValue>>([new DailyValue(to, 0.05m)]);
    }
}
