namespace Finance.Api.Domain.MarketData;

/// <summary>
/// One daily value of a benchmark series (CDI, SELIC, IPCA, USDBRL, IVVB11).
/// Shared market data: no <c>UserId</c>, no query filter.
/// </summary>
/// <remarks>
/// Keyed by <c>(Code, Date)</c>; a re-sync of the same day overwrites.
/// <see cref="Value"/> is in the series' own unit (a daily or monthly percentage, an
/// index level, a rate), recorded beside each code in configuration.
/// </remarks>
public sealed class Benchmark
{
    /// <summary>
    /// The series that converts a USD amount to BRL: valuing a USD asset, and pricing an
    /// AI call billed in dollars. 006 stores it from BCB SGS series 1.
    /// </summary>
    public const string UsdBrl = "USDBRL";

    public string Code { get; set; } = "";

    public DateOnly Date { get; set; }

    public decimal Value { get; set; }
}
