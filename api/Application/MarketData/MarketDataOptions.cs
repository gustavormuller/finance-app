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

    /// <summary>How far back the first sync of an asset or series reaches.</summary>
    public int BackfillYears { get; set; }

    public BcbOptions Bcb { get; set; } = new();

    public BrapiOptions Brapi { get; set; } = new();

    public CoinGeckoOptions CoinGecko { get; set; } = new();

    public TwelveDataOptions TwelveData { get; set; } = new();
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
