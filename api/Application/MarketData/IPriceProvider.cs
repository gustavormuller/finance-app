using Finance.Api.Domain.MarketData;

namespace Finance.Api.Application.MarketData;

/// <summary>
/// A source of an asset's daily closes (006, decision 2; ADR-015). brapi, CoinGecko and
/// Twelve Data implement it; <see cref="IPriceProviderRegistry"/> picks one by
/// <see cref="MarketAsset.Provider"/>.
/// </summary>
/// <remarks>
/// Returns only what the provider reports inside <c>[from, to]</c>: weekends and holidays
/// are absent, never filled in (decision 5). A symbol the provider does not know is an
/// empty list. A <c>429</c> is <see cref="ProviderRateLimitedException"/>; a body that
/// cannot be read is <see cref="ProviderResponseInvalidException"/>.
/// </remarks>
public interface IPriceProvider
{
    ProviderKind Kind { get; }

    Task<IReadOnlyList<DailyClose>> GetDailyClosesAsync(
        string providerSymbol, DateOnly from, DateOnly to, CancellationToken ct);
}
