using System.Globalization;
using System.Net;
using System.Text.Json;
using Finance.Api.Application.MarketData;

namespace Finance.Api.Infrastructure.MarketData;

/// <summary>
/// What the four adapters do alike with a response: a <c>429</c> becomes
/// <see cref="ProviderRateLimitedException"/>, and any failure to read the body becomes
/// <see cref="ProviderResponseInvalidException"/> (006, tests 7 and 8). Numbers go from
/// the JSON text straight to <c>decimal</c>, never through <c>double</c> (test 9).
/// </summary>
internal static class ProviderResponse
{
    /// <summary>Throws on <c>429</c>; any other status is the caller's to judge.</summary>
    public static void ThrowIfRateLimited(HttpResponseMessage response, string provider)
    {
        if (response.StatusCode == HttpStatusCode.TooManyRequests)
        {
            throw new ProviderRateLimitedException(provider, response.Headers.RetryAfter?.Delta);
        }
    }

    /// <summary>
    /// Parses the body and hands its root to <paramref name="read"/>. Malformed JSON, a
    /// missing property, a wrong kind or a value that does not parse all surface as
    /// <see cref="ProviderResponseInvalidException"/>; nothing else escapes.
    /// </summary>
    public static async Task<T> ReadAsync<T>(
        HttpResponseMessage response, string provider, Func<JsonElement, T> read, CancellationToken ct)
    {
        try
        {
            await using var body = await response.Content.ReadAsStreamAsync(ct);
            using var document = await JsonDocument.ParseAsync(body, cancellationToken: ct);
            return read(document.RootElement);
        }
        catch (Exception error) when (error is JsonException or FormatException or InvalidOperationException
            or KeyNotFoundException or IndexOutOfRangeException or ArgumentException or OverflowException)
        {
            throw new ProviderResponseInvalidException(provider, error.Message, error);
        }
    }

    /// <summary>
    /// A JSON number or a numeric string, read as <c>decimal</c> with the invariant culture.
    /// </summary>
    public static decimal Decimal(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Number => value.GetDecimal(),
        JsonValueKind.String => decimal.Parse(
            value.GetString()!, NumberStyles.AllowLeadingSign | NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture),
        _ => throw new FormatException($"Expected a number, found {value.ValueKind}."),
    };

    /// <summary>A date string in exactly <paramref name="format"/>, invariant culture.</summary>
    public static DateOnly Date(JsonElement value, string format) =>
        DateOnly.ParseExact(value.GetString() ?? "", format, CultureInfo.InvariantCulture, DateTimeStyles.None);

    /// <summary>Only the days inside <c>[from, to]</c>, oldest first.</summary>
    public static IReadOnlyList<T> Within<T>(IEnumerable<T> points, Func<T, DateOnly> date, DateOnly from, DateOnly to) =>
        [.. points.Where(point => date(point) >= from && date(point) <= to).OrderBy(date)];
}
