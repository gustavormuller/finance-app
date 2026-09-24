namespace Finance.Api.Application.MarketData;

/// <summary>
/// The <c>MarketData</c> configuration section (006). Series codes, endpoints and keys
/// are configuration, not code: a wrong SGS code is a config edit. Defaults live in
/// <c>appsettings.json</c>; keys come from user secrets or the environment.
/// </summary>
public sealed class MarketDataOptions
{
    public const string Section = "MarketData";

    /// <summary>Cron expression, server local time.</summary>
    public string Schedule { get; set; } = "";

    /// <summary>
    /// Whether the nightly job runs at all. <c>true</c> in <c>appsettings.json</c>; the test
    /// hosts and the E2E run switch it off, so no test ever syncs against the network.
    /// </summary>
    public bool ScheduledSync { get; set; }

    /// <summary>
    /// E2E only: fixed, network-free providers in place of the real ones, and no
    /// ten-minute window between manual syncs, because the E2E database is shared and
    /// kept between runs. Refused at boot outside Development.
    /// </summary>
    public bool FakeProviders { get; set; }

    /// <summary>How far back the first sync of an asset or series reaches.</summary>
    public int BackfillYears { get; set; }

    public BcbOptions Bcb { get; set; } = new();

    /// <summary>
    /// Benchmarks served as an asset's closes by a price provider, stored in
    /// <c>Benchmarks</c> under their code: <c>IVVB11</c> from brapi, an S&amp;P 500 proxy in BRL.
    /// </summary>
    public Dictionary<string, PriceBenchmark> PriceBenchmarks { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public BrapiOptions Brapi { get; set; } = new();

    public CoinGeckoOptions CoinGecko { get; set; } = new();

    public TwelveDataOptions TwelveData { get; set; } = new();

    public MarketDataResilienceOptions Resilience { get; set; } = new();
}

/// <summary>
/// Each provider's own retry and circuit breaker (006, decision 4). Every provider has its
/// own instance, so brapi down never opens CoinGecko's circuit.
/// </summary>
public sealed class MarketDataResilienceOptions
{
    /// <summary>Retries after the first attempt, for a 5xx, a 408, a timeout or a network failure. Never for a 429.</summary>
    public int RetryAttempts { get; set; } = 3;

    /// <summary>The first retry's delay; each next one doubles it, with jitter.</summary>
    public TimeSpan RetryBaseDelay { get; set; } = TimeSpan.FromSeconds(2);

    /// <summary>Attempts that, all failing inside <see cref="SamplingDuration"/>, open the circuit.</summary>
    public int FailuresToBreak { get; set; } = 5;

    public TimeSpan SamplingDuration { get; set; } = TimeSpan.FromMinutes(5);

    /// <summary>How long an open circuit answers without calling the provider.</summary>
    public TimeSpan BreakDuration { get; set; } = TimeSpan.FromMinutes(5);

    public TimeSpan AttemptTimeout { get; set; } = TimeSpan.FromSeconds(30);

    /// <summary>One call, retries included. Replaces <see cref="HttpClient.Timeout"/>, which is switched off.</summary>
    public TimeSpan TotalTimeout { get; set; } = TimeSpan.FromMinutes(3);
}

/// <summary>BCB SGS. <see cref="BaseUrl"/> is a prefix the series code is appended to.</summary>
public sealed class BcbOptions
{
    public string BaseUrl { get; set; } = "";

    /// <summary>Benchmark code (<c>CDI</c>) to its SGS series and unit.</summary>
    public Dictionary<string, SgsSeries> Series { get; set; } = new(StringComparer.OrdinalIgnoreCase);
}

/// <summary>One SGS series: its number on the portal and the unit its values are in.</summary>
public sealed class SgsSeries
{
    public int Code { get; set; }

    public BenchmarkUnit Unit { get; set; }
}

/// <summary>A benchmark read through an <see cref="IPriceProvider"/>: which one, its symbol there, and the unit.</summary>
public sealed class PriceBenchmark
{
    public Domain.MarketData.ProviderKind Provider { get; set; }

    public string Symbol { get; set; } = "";

    public BenchmarkUnit Unit { get; set; }
}

/// <summary>
/// What a benchmark value means. 008 accumulates by it: rates compound, levels divide.
/// </summary>
public enum BenchmarkUnit
{
    /// <summary>A rate in percent for one business day, e.g. CDI <c>0.043739</c> = 0.043739% that day.</summary>
    PercentPerDay = 0,

    /// <summary>A rate in percent for the month the date opens, e.g. IPCA <c>0.42</c>.</summary>
    PercentPerMonth = 1,

    /// <summary>A level (index points, an exchange rate, a price): the return is the ratio of two values.</summary>
    Level = 2,
}

public sealed class BrapiOptions
{
    public string BaseUrl { get; set; } = "";

    /// <summary>Sent as a bearer token, never in the query string, so it stays out of logged URLs.</summary>
    public string Token { get; set; } = "";
}

public sealed class CoinGeckoOptions
{
    public string BaseUrl { get; set; } = "";

    /// <summary>Sent as the <c>x-cg-demo-api-key</c> header.</summary>
    public string DemoKey { get; set; } = "";

    /// <summary>The quote currency; the catalogue's <c>Currency</c> of a CoinGecko asset must match it.</summary>
    public string VsCurrency { get; set; } = "";

    /// <summary>How far back the plan serves history; older requested days are not asked for.</summary>
    public int MaxHistoryDays { get; set; }
}

public sealed class TwelveDataOptions
{
    public string BaseUrl { get; set; } = "";

    /// <summary>Sent as <c>Authorization: apikey ...</c>, never in the query string.</summary>
    public string Key { get; set; } = "";
}
