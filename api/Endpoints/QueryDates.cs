using System.Globalization;

namespace Finance.Api.Endpoints;

/// <summary>
/// The <c>from</c>/<c>to</c> query parameters the returns and the comparison take, bound as
/// strings and parsed here, so a malformed one is a 400 with one pt-BR wording on both routes.
/// </summary>
internal static class QueryDates
{
    public const string FromFormat = "A data inicial deve estar no formato AAAA-MM-DD, por exemplo 2026-01-31.";

    public const string ToFormat = "A data final deve estar no formato AAAA-MM-DD, por exemplo 2026-01-31.";

    /// <summary>Dates beside a preset period are refused rather than silently ignored.</summary>
    public const string OnlyForCustomPeriod = "As datas de início e fim valem apenas para o período personalizado (custom).";

    private const string Format = "yyyy-MM-dd";

    /// <summary>The date, or <c>null</c> when absent or <paramref name="malformed"/>.</summary>
    public static DateOnly? Parse(string? value, out bool malformed)
    {
        malformed = false;
        if (value is null)
        {
            return null;
        }

        if (DateOnly.TryParseExact(value, Format, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        malformed = true;
        return null;
    }
}
