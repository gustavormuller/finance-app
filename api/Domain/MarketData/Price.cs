namespace Finance.Api.Domain.MarketData;

/// <summary>
/// One daily close of a <see cref="MarketAsset"/>, in the asset's own currency.
/// Shared market data: no <c>UserId</c>, no query filter.
/// </summary>
/// <remarks>
/// Keyed by <c>(MarketAssetId, Date)</c>; a re-sync of the same day overwrites. Only
/// what the provider returns is stored: weekends and holidays are absent rows, and
/// carrying a close forward is a read concern (006, decision 5).
/// </remarks>
public sealed class Price
{
    public Guid MarketAssetId { get; set; }

    public DateOnly Date { get; set; }

    public decimal Close { get; set; }
}
