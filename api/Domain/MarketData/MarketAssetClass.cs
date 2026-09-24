namespace Finance.Api.Domain.MarketData;

/// <summary>What kind of instrument a <see cref="MarketAsset"/> is.</summary>
/// <remarks>
/// Stored as <c>int</c>, with the values written down: renumbering them later would
/// silently reinterpret every catalogue row. Fixed income is absent on purpose: it
/// has no market price (006, out of scope).
/// </remarks>
public enum MarketAssetClass
{
    StockBr = 0,
    Fii = 1,
    EtfBr = 2,
    Bdr = 3,
    StockUs = 4,
    Crypto = 5,
}
