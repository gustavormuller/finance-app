namespace Finance.Api.Domain.MarketData;

/// <summary>
/// One priced instrument in the shared catalogue. Deliberately not
/// <see cref="IUserOwned"/>: two people holding PETR4 share one row and one price
/// series, so more users never mean more calls to a provider.
/// </summary>
/// <remarks>
/// Unique by <c>(Provider, ProviderSymbol)</c>: the same ticker may exist at two
/// providers, but a provider's symbol names one series.
/// </remarks>
public sealed class MarketAsset
{
    public Guid Id { get; set; }

    /// <summary>What a person reads, e.g. <c>PETR4</c>, <c>BTC</c>, <c>AAPL</c>.</summary>
    public string Ticker { get; set; } = "";

    public string Name { get; set; } = "";

    public MarketAssetClass Class { get; set; }

    /// <summary>The currency the provider quotes it in, <c>BRL</c> or <c>USD</c>. Prices are stored in it, converted on read.</summary>
    public string Currency { get; set; } = "";

    public ProviderKind Provider { get; set; }

    /// <summary>What the provider calls it, e.g. <c>bitcoin</c> at CoinGecko.</summary>
    public string ProviderSymbol { get; set; } = "";

    /// <summary>Inactive assets are skipped by the sync.</summary>
    public bool IsActive { get; set; }

    public DateTimeOffset? LastSyncedAt { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
