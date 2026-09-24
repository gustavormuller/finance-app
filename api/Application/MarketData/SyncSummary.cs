namespace Finance.Api.Application.MarketData;

/// <summary>
/// One provider's part of <see cref="Domain.MarketData.SyncRun.Summary"/>, keyed by the
/// provider's name (<c>Brapi</c>, <c>CoinGecko</c>, <c>TwelveData</c>, <c>Bcb</c>). Every
/// <see cref="SyncFailure.Error"/> is pt-BR text for the screen, chosen by the failure's
/// type; the exception's own message goes to the log only.
/// </summary>
public sealed class ProviderSyncSummary
{
    public int RowsWritten { get; set; }

    /// <summary>Assets and benchmark series fetched and stored without error.</summary>
    public int ItemsSynced { get; set; }

    public int ItemsFailed { get; set; }

    /// <summary>The first failure's text, or <c>null</c> when the provider had none.</summary>
    public string? Error { get; set; }

    public List<SyncFailure> Failures { get; set; } = [];
}

/// <summary>An asset (its ticker) or a benchmark (its code) that could not be synced, and why, in pt-BR.</summary>
public sealed record SyncFailure(string Item, string Error);

/// <summary>The JSON of <see cref="Domain.MarketData.SyncRun.Summary"/>: provider name to its part, camelCase.</summary>
public static class SyncSummaryJson
{
    private static readonly System.Text.Json.JsonSerializerOptions Options = new(System.Text.Json.JsonSerializerDefaults.Web);

    public static string Write(IReadOnlyDictionary<string, ProviderSyncSummary> summary) =>
        System.Text.Json.JsonSerializer.Serialize(summary, Options);

    public static Dictionary<string, ProviderSyncSummary> Read(string json) =>
        System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, ProviderSyncSummary>>(json, Options) ?? [];
}
