using Finance.Api.Application.MarketData;
using Finance.Api.Domain.MarketData;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// A price provider that answers from a function and records what it was asked. By
/// default it returns one close, on <c>to</c>, so every fetched asset writes one row.
/// </summary>
internal sealed class FakePriceProvider(ProviderKind kind) : IPriceProvider
{
    public ProviderKind Kind => kind;

    public List<(string Symbol, DateOnly From, DateOnly To)> Calls { get; } = [];

    public Func<string, DateOnly, DateOnly, IReadOnlyList<DailyClose>> Respond { get; set; } =
        (_, _, to) => [new DailyClose(to, 10m)];

    public async Task<IReadOnlyList<DailyClose>> GetDailyClosesAsync(
        string providerSymbol, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await Task.Yield();
        Calls.Add((providerSymbol, from, to));
        return Respond(providerSymbol, from, to);
    }
}

/// <summary>The benchmark-port twin of <see cref="FakePriceProvider"/>.</summary>
internal sealed class FakeBenchmarkProvider : IBenchmarkProvider
{
    public List<(string Code, DateOnly From, DateOnly To)> Calls { get; } = [];

    public Func<string, DateOnly, DateOnly, IReadOnlyList<DailyValue>> Respond { get; set; } =
        (_, _, to) => [new DailyValue(to, 0.05m)];

    public async Task<IReadOnlyList<DailyValue>> GetSeriesAsync(
        string code, DateOnly from, DateOnly to, CancellationToken ct)
    {
        await Task.Yield();
        Calls.Add((code, from, to));
        return Respond(code, from, to);
    }
}
