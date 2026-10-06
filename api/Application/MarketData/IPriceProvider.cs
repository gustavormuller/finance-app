using Finance.Api.Domain.MarketData;

namespace Finance.Api.Application.MarketData;

/// <summary>
/// A source of an asset's daily closes (006, decision 2; ADR-015). Yahoo, brapi, CoinGecko,
/// Twelve Data and Binance implement it; <see cref="IPriceProviderRegistry"/> picks one by
/// <see cref="MarketAsset.Provider"/>.
/// </summary>
/// <remarks>
/// Returns only what the provider reports inside <c>[from, to]</c>: weekends and holidays
/// are absent, never filled in (decision 5). A symbol the provider does not know is an
/// empty list, or <see cref="ProviderSymbolUnknownException"/> where the provider says so
/// plainly (Yahoo). A <c>429</c> is <see cref="ProviderRateLimitedException"/>; a body that
/// cannot be read is <see cref="ProviderResponseInvalidException"/>.
/// </remarks>
public interface IPriceProvider
{
    ProviderKind Kind { get; }

    /// <summary>
    /// The first day a load of an asset's whole history asks for (025): Yahoo serves decades
    /// in one request. Null: <see cref="MarketDataOptions.BackfillYears"/> back.
    /// </summary>
    DateOnly? HistoryStart => null;

    /// <summary>
    /// Whether days already stored can change at the source (025): Yahoo rebases its adjusted
    /// close at every dividend and split, and corrects data. The sync then re-reads the last
    /// days it stored and reloads the whole history when one no longer matches.
    /// </summary>
    bool RevisesHistory => false;

    Task<IReadOnlyList<DailyClose>> GetDailyClosesAsync(
        string providerSymbol, DateOnly from, DateOnly to, CancellationToken ct);
}
