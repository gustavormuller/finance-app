namespace Finance.Api.Application.MarketData;

/// <summary>
/// A source of a benchmark series' daily values, by code (<c>CDI</c>, <c>SELIC</c>,
/// <c>IPCA</c>, <c>USDBRL</c>). BCB SGS is the one implementation today; ADR-015 keeps the
/// port for a foreseen second source (USDBRL or IVVB11 from brapi or Twelve Data).
/// </summary>
/// <remarks>
/// Values are in the series' own unit, recorded beside its code in
/// <c>MarketData:Bcb:Series</c>. Same contract as <see cref="IPriceProvider"/> for gaps,
/// rate limits and unreadable bodies.
/// </remarks>
public interface IBenchmarkProvider
{
    Task<IReadOnlyList<DailyValue>> GetSeriesAsync(
        string code, DateOnly from, DateOnly to, CancellationToken ct);
}
