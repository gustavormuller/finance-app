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
/// Yahoo has a fake of its own, <see cref="FakeYahooProvider"/>.
/// </remarks>
public static class FakeMarketDataProviders
{
    public static IPriceProviderRegistry Registry { get; } = new PriceProviderRegistry(
        Enum.GetValues<ProviderKind>().Select(kind => kind == ProviderKind.Yahoo
            ? (IPriceProvider)new FakeYahooProvider()
            : new FakePriceProvider(kind)));

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

    /// <summary>
    /// Yahoo's shape (025, decision 21): a whole history of 1000 days before <c>to</c>, asked
    /// for from 1900 and revised like Yahoo's. The split-adjusted price is 12.50 on <c>to</c>,
    /// one cent lower for each day before it and 7.50 from 500 days back. The close is the
    /// raw traded price, twice that before a 2:1 split on <c>to - 100</c>. The adjusted close
    /// loses 1 % for each of the dividends on <c>to - 30</c>, <c>- 60</c> and <c>- 90</c> still
    /// ahead of its day.
    /// </summary>
    private sealed class FakeYahooProvider : IPriceProvider
    {
        private const int Days = 1000;

        private static readonly int[] DividendsBeforeTo = [30, 60, 90];

        private const int SplitBeforeTo = 100;

        public ProviderKind Kind => ProviderKind.Yahoo;

        public DateOnly? HistoryStart => YahooProvider.WholeHistory;

        public bool RevisesHistory => true;

        public Task<IReadOnlyList<DailyClose>> GetDailyClosesAsync(
            string providerSymbol, DateOnly from, DateOnly to, CancellationToken ct)
        {
            var first = from > to.AddDays(-Days) ? from : to.AddDays(-Days);
            return Task.FromResult<IReadOnlyList<DailyClose>>(
                Enumerable.Range(0, Math.Max(0, to.DayNumber - first.DayNumber + 1))
                    .Select(i => first.AddDays(i))
                    .Select(date =>
                    {
                        var daysBefore = to.DayNumber - date.DayNumber;
                        var price = 12.50m - 0.01m * Math.Min(daysBefore, 500);
                        var dividendsAhead = DividendsBeforeTo.Count(dividend => dividend < daysBefore);
                        return new DailyClose(
                            date,
                            daysBefore > SplitBeforeTo ? price * 2 : price,
                            price * Enumerable.Repeat(0.99m, dividendsAhead).Aggregate(1m, (product, factor) => product * factor));
                    })
                    .ToList());
        }
    }

    private sealed class FakeBenchmarkProvider : IBenchmarkProvider
    {
        public Task<IReadOnlyList<DailyValue>> GetSeriesAsync(
            string code, DateOnly from, DateOnly to, CancellationToken ct) =>
            Task.FromResult<IReadOnlyList<DailyValue>>([new DailyValue(to, 0.05m)]);
    }
}
