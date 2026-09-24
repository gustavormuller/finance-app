using System.Globalization;
using System.Text.Json;
using Finance.Api.Application.Ai;

namespace Finance.Api.Infrastructure.Ai;

/// <summary>
/// What the two adapters do alike on the wire. Every failure becomes
/// <see cref="AiProviderException"/> with the tokens known to be billed; cancellation is
/// left alone, so <see cref="AiGateway"/> can tell its timeout from the caller leaving.
/// Messages are English, for logs, and never hold the key.
/// </summary>
internal static class AiHttp
{
    /// <summary>
    /// An empty key fails the call, not the boot: AI is off by default and dev and test
    /// hosts have none. Nothing is sent, so nothing is billed.
    /// </summary>
    public static void RequireKey(string key, string provider, string setting)
    {
        if (string.IsNullOrWhiteSpace(key))
        {
            throw new AiProviderException($"{provider}: {setting} is empty; set it in user secrets or the environment.", inputTokens: 0);
        }
    }

    /// <summary>Sends once, no retry. A transport failure has unknown tokens: the gateway estimates them.</summary>
    public static async Task<HttpResponseMessage> SendAsync(HttpClient http, HttpRequestMessage message, string provider, CancellationToken ct)
    {
        try
        {
            return await http.SendAsync(message, ct);
        }
        catch (HttpRequestException error)
        {
            throw new AiProviderException($"{provider} could not be reached: {error.Message}", inputTokens: null, innerException: error);
        }
    }

    /// <summary>
    /// A non-success status. A 4xx is refused before inference, so it billed nothing; a 5xx
    /// (or anything else) is unknown. The body's error type and message go into the text when
    /// it is JSON; a 404 also names the model, the usual cause being an id of the other provider.
    /// </summary>
    public static async Task<AiProviderException> FailureAsync(
        HttpResponseMessage response, string provider, AiRequest request, string key, CancellationToken ct)
    {
        var status = (int)response.StatusCode;
        var (type, detail) = await ErrorAsync(response, ct);

        var text = $"{provider} answered {status.ToString(CultureInfo.InvariantCulture)}"
            + (type is null ? "" : $" {type}")
            + (detail is null ? "." : $": {Truncate(detail)}");
        if (status == 404)
        {
            text += $" Check that the model '{request.Model}' exists for Ai:Provider '{provider.ToLowerInvariant()}'.";
        }

        if (!string.IsNullOrEmpty(key))
        {
            text = text.Replace(key, "[redacted]", StringComparison.Ordinal);
        }

        return new AiProviderException(text, inputTokens: status is >= 400 and < 500 ? 0 : null);
    }

    /// <summary>
    /// Parses a success body and hands its root to <paramref name="read"/>. A body that is not
    /// the documented shape has unknown tokens.
    /// </summary>
    public static async Task<T> ReadAsync<T>(HttpResponseMessage response, string provider, Func<JsonElement, T> read, CancellationToken ct)
    {
        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: ct);
            return read(document.RootElement);
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException
            or KeyNotFoundException or IndexOutOfRangeException or ArgumentException)
        {
            throw new AiProviderException($"{provider} returned a response that could not be read: {error.Message}", inputTokens: null, innerException: error);
        }
    }

    // Both providers nest it the same way: {"error":{"type":"...","message":"..."}}.
    private static async Task<(string? Type, string? Message)> ErrorAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var body = await response.Content.ReadAsStringAsync(ct);
        try
        {
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.ValueKind == JsonValueKind.Object
                && document.RootElement.TryGetProperty("error", out var error)
                && error.ValueKind == JsonValueKind.Object)
            {
                return (StringOrNull(error, "type"), StringOrNull(error, "message"));
            }
        }
        catch (JsonException)
        {
            // A proxy's HTML page, say: the status alone says enough.
        }

        return (null, null);
    }

    private static string? StringOrNull(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

    private static string Truncate(string text) => text.Length <= 300 ? text : text[..300] + "...";
}
