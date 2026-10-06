using Finance.Api.Domain.MarketData;

namespace Finance.Api.Application.Compare;

/// <summary>What a stored price contributes to a comparison (026, decision 4).</summary>
public static class ComparisonPrice
{
    /// <summary>
    /// The total return, dividends reinvested: the adjusted close where the provider sends one
    /// (Yahoo, 025), else the traded close. The coalesce is per row; 025 replaces an asset's
    /// prices whole when its source changes, so one series never mixes the two.
    /// </summary>
    public static decimal ValueOf(Price price) => price.AdjustedClose ?? price.Close;
}
