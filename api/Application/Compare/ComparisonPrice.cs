using Finance.Api.Domain.MarketData;

namespace Finance.Api.Application.Compare;

/// <summary>What a stored price contributes to a comparison (026, decision 4).</summary>
public static class ComparisonPrice
{
    /// <summary>
    /// The close. Once prices carry an adjusted close (025), this line becomes
    /// <c>price.AdjustedClose ?? price.Close</c>: the total return, dividends reinvested.
    /// </summary>
    public static decimal ValueOf(Price price) => price.Close;
}
