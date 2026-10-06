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

    /// <summary>
    /// The raw traded price: what the shares traded at that day, before any later split.
    /// A split is a movement that adds quantity (007), so the snapshots value
    /// <c>Quantity * Close</c> and need the price the quantity held then traded at.
    /// </summary>
    public decimal Close { get; set; }

    /// <summary>
    /// The close adjusted for every later split and dividend, the total-return series (025).
    /// Only Yahoo sends one; null for the other providers. It is rebased backwards at every
    /// new dividend, so only ratios between its days mean anything.
    /// </summary>
    public decimal? AdjustedClose { get; set; }
}
