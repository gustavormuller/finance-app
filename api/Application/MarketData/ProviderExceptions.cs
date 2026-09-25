namespace Finance.Api.Application.MarketData;

/// <summary>
/// A market-data provider could not deliver a series. The sync catches it per asset and
/// per provider, so one bad ticker or one provider down never stops the rest (006).
/// </summary>
/// <remarks>
/// Messages are English diagnostics for logs, not text for the screen: whatever the sync
/// shows a person is its own, in pt-BR.
/// </remarks>
public abstract class MarketDataProviderException(string provider, string message, Exception? inner = null)
    : Exception(message, inner)
{
    /// <summary>Which provider failed, e.g. <c>Brapi</c> or <c>BcbSgs</c>.</summary>
    public string Provider { get; } = provider;
}

/// <summary>The provider answered <c>429</c> (006, test 7): back off, do not retry at once.</summary>
public sealed class ProviderRateLimitedException(string provider, TimeSpan? retryAfter)
    : MarketDataProviderException(provider, $"{provider} rate-limited the request.")
{
    /// <summary>The provider's <c>Retry-After</c>, when it sent one.</summary>
    public TimeSpan? RetryAfter { get; } = retryAfter;
}

/// <summary>
/// The provider refused a request (<c>401</c> or <c>403</c>) and no key for it is
/// configured (019): brapi outside its four keyless tickers, Twelve Data for anything,
/// CoinGecko if it stops serving keyless calls. With a key configured, the same refusal
/// is about that key and stays an <see cref="HttpRequestException"/>.
/// </summary>
public sealed class ProviderKeyMissingException(string provider, string symbol, string setting)
    : MarketDataProviderException(provider, $"{provider} refused {symbol} and no key is configured; set {setting}.")
{
    /// <summary>What the provider was asked for, e.g. <c>BBAS3</c> or <c>bitcoin</c>.</summary>
    public string Symbol { get; } = symbol;

    /// <summary>The configuration key to fill in, e.g. <c>MarketData:Brapi:Token</c>.</summary>
    public string Setting { get; } = setting;
}

/// <summary>
/// The provider answered with a body that is not the documented shape: malformed JSON, a
/// missing field, a value that is not a number or a date (006, test 8).
/// </summary>
public sealed class ProviderResponseInvalidException(string provider, string detail, Exception? inner = null)
    : MarketDataProviderException(provider, $"{provider} returned a response that could not be read: {detail}", inner);
