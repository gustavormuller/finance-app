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

    /// <summary>A market index such as <c>^BVSP</c>: compared against, never held (025).</summary>
    Index = 6,

    /// <summary>An exchange rate such as <c>BRL=X</c>: compared against, never held (025).</summary>
    Currency = 7,
}

/// <summary>Which classes a person can hold in the portfolio (007).</summary>
public static class MarketAssetClasses
{
    /// <summary>False for what exists only to be compared with: indices and exchange rates (025).</summary>
    public static bool CanBeHeld(this MarketAssetClass assetClass) =>
        assetClass is not (MarketAssetClass.Index or MarketAssetClass.Currency);
}
