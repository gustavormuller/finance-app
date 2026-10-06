namespace Finance.Api.Domain.MarketData;

/// <summary>The external source a <see cref="MarketAsset"/>'s prices come from.</summary>
/// <remarks>
/// Stored as <c>int</c>, values written down. BCB is not here: it serves benchmark
/// series, never an asset's prices (006, decision 2).
/// </remarks>
public enum ProviderKind
{
    Brapi = 0,
    CoinGecko = 1,
    TwelveData = 2,

    /// <summary>Binance's public spot market, for crypto pairs quoted in BRL (019). Needs no key.</summary>
    Binance = 3,

    /// <summary>
    /// Yahoo Finance's chart endpoint (025): B3, US stocks, indices, crypto in USD and FX, with
    /// whole histories. Needs no key; unofficial and for personal use only.
    /// </summary>
    Yahoo = 4,
}
