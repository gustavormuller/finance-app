namespace Finance.Api.Application.Ai;

/// <summary>
/// The <c>Ai</c> configuration section. Model identifiers and their prices are
/// configuration, never code (009, decision 2). Prices are the providers' USD list prices
/// (decided by the user), converted to BRL at the latest <c>USDBRL</c> benchmark or
/// <see cref="UsdBrl"/>. Keys come from user secrets or the environment.
/// </summary>
public sealed class AiOptions
{
    public const string Section = "Ai";

    /// <summary><c>anthropic</c> or <c>openai</c>.</summary>
    public string Provider { get; set; } = "";

    /// <summary>Per user and calendar month, in BRL. The hard ceiling of ADR-008.</summary>
    public decimal MonthlyBudgetBrl { get; set; }

    public AiTaskOptions Categorisation { get; set; } = new();

    public AiTaskOptions Analysis { get; set; } = new();

    /// <summary>Model identifier to its USD list prices. Every configured model needs one.</summary>
    public Dictionary<string, AiModelPrice> Pricing { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public AiProviderOptions Anthropic { get; set; } = new();

    public AiProviderOptions OpenAi { get; set; } = new();

    /// <summary>BRL per USD, used only when no <c>USDBRL</c> benchmark has been synced.</summary>
    public decimal UsdBrl { get; set; }

    /// <summary>
    /// E2E only: a fixed, network-free provider in place of the configured one. Refused at
    /// boot outside Development.
    /// </summary>
    public bool FakeProvider { get; set; }

    /// <summary>
    /// The analysis job's sweep of rows a restart left <c>Pending</c> or <c>Running</c>. On
    /// unless switched off; the test hosts switch it off, as they do the nightly sync.
    /// </summary>
    public bool AnalysisSweep { get; set; } = true;

    /// <summary>Empty when the configuration is sound; otherwise English diagnostics for the operator.</summary>
    public static IReadOnlyList<string> Problems(AiOptions options)
    {
        var problems = new List<string>();
        if (!Providers.Contains(options.Provider, StringComparer.OrdinalIgnoreCase))
        {
            problems.Add($"{Section}:Provider is '{options.Provider}'; it must be one of {string.Join(", ", Providers)}.");
        }

        if (options.MonthlyBudgetBrl < 0m)
        {
            problems.Add($"{Section}:MonthlyBudgetBrl is negative; it is each user's monthly ceiling in BRL.");
        }

        if (options.UsdBrl <= 0m)
        {
            problems.Add($"{Section}:UsdBrl must be positive; it prices calls when no USDBRL benchmark has been synced.");
        }

        foreach (var (key, adapter) in new[] { ("Anthropic", options.Anthropic), ("OpenAi", options.OpenAi) })
        {
            if (!Uri.TryCreate(adapter.BaseUrl, UriKind.Absolute, out var url)
                || (url.Scheme != Uri.UriSchemeHttps && url.Scheme != Uri.UriSchemeHttp)
                || !url.AbsolutePath.EndsWith('/'))
            {
                problems.Add($"{Section}:{key}:BaseUrl is '{adapter.BaseUrl}'; it must be an absolute http(s) URL ending in '/'.");
            }
        }

        foreach (var (key, task) in new[] { ("Categorisation", options.Categorisation), ("Analysis", options.Analysis) })
        {
            if (task.TimeoutSeconds <= 0)
            {
                problems.Add($"{Section}:{key}:TimeoutSeconds must be positive; it bounds each provider call.");
            }

            if (string.IsNullOrWhiteSpace(task.Model))
            {
                problems.Add($"{Section}:{key}:Model is empty.");
            }
            else if (!options.Pricing.TryGetValue(task.Model, out var price))
            {
                problems.Add($"{Section}:Pricing:{task.Model} is missing; {Section}:{key}:Model names it, and a call "
                    + "with no price would cost nothing against the budget. (A model id with ':' cannot be a key.)");
            }
            else if (price.InputPerMTokUsd <= 0m || price.OutputPerMTokUsd <= 0m)
            {
                problems.Add($"{Section}:Pricing:{task.Model} needs positive InputPerMTokUsd and OutputPerMTokUsd.");
            }
        }

        return problems.Distinct().ToList();
    }

    /// <summary>Fails the boot on any of <see cref="Problems"/>, so no call is ever priced at zero.</summary>
    public static void RefuseInvalid(IConfiguration configuration)
    {
        var options = configuration.GetSection(Section).Get<AiOptions>() ?? new AiOptions();

        var problems = Problems(options);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", problems));
        }
    }

    private static readonly string[] Providers = ["anthropic", "openai"];
}

/// <summary>One use of the AI: the model it calls and how long a call may take.</summary>
public sealed class AiTaskOptions
{
    public string Model { get; set; } = "";

    /// <summary>
    /// The whole provider call, enforced by <see cref="AiGateway"/> for every adapter. Past it
    /// the call fails with <c>AiProviderTimeoutException</c>. Categorisation: 30 s
    /// (009, decision 4).
    /// </summary>
    public int TimeoutSeconds { get; set; }
}

/// <summary>A model's list prices, in USD per million tokens. Money: <c>decimal</c>.</summary>
public sealed class AiModelPrice
{
    public decimal InputPerMTokUsd { get; set; }

    public decimal OutputPerMTokUsd { get; set; }
}

public sealed class AiProviderOptions
{
    /// <summary>Sent in a header only, never in a URL or a log (ADR-015, 006's pattern).</summary>
    public string ApiKey { get; set; } = "";

    /// <summary>The API root, ending in <c>/</c>; the adapter appends <c>v1/...</c>.</summary>
    public string BaseUrl { get; set; } = "";
}
