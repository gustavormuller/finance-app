using System.Globalization;

namespace Finance.Api.Domain.Import;

/// <summary>
/// Text to <see cref="decimal"/>, under a format the template declared. Never
/// <see cref="double"/>, never <c>Convert</c>, never a guess at the separators.
/// </summary>
/// <remarks>
/// The spec says "<c>decimal.Parse</c> with an explicit <c>CultureInfo</c> from the
/// template". The API runs with <c>InvariantGlobalization</c>, where
/// <c>CultureInfo.GetCultureInfo("pt-BR")</c> throws, so the culture name selects an
/// explicit <see cref="NumberFormatInfo"/> written down here instead. It is still
/// <c>decimal.TryParse</c> with an explicit provider; the separators are simply ours
/// rather than ICU's, which also means they cannot change under us with an ICU
/// upgrade on the server.
/// </remarks>
public static class AmountParser
{
    /// <summary>
    /// Thousands, decimals, leading or trailing sign, surrounding whitespace, a
    /// currency symbol and accounting parentheses. Not exponents: <c>1e3</c> in a
    /// statement is garbage, not a thousand.
    /// </summary>
    private const NumberStyles Styles =
        NumberStyles.Number | NumberStyles.AllowCurrencySymbol | NumberStyles.AllowParentheses;

    private static readonly IReadOnlyDictionary<string, NumberFormatInfo> Formats =
        new Dictionary<string, NumberFormatInfo>(StringComparer.Ordinal)
        {
            ["pt-BR"] = Format(decimalSeparator: ",", groupSeparator: ".", currencySymbol: "R$"),
            ["en-US"] = Format(decimalSeparator: ".", groupSeparator: ",", currencySymbol: "$"),
        };

    public static IReadOnlyCollection<string> SupportedCultures => Formats.Keys.ToArray();

    /// <summary>Case-sensitive: the template stores the name exactly as offered.</summary>
    public static bool IsSupportedCulture(string? culture) =>
        culture is not null && Formats.ContainsKey(culture);

    /// <summary>
    /// False, and zero, for anything that does not fit the declared format — including
    /// a number written in the other culture's separators, which must not become a
    /// different number.
    /// </summary>
    /// <exception cref="ArgumentException">
    /// The culture is not one of <see cref="SupportedCultures"/>. Templates are
    /// validated before they are saved, so reaching this is a programming error.
    /// </exception>
    public static bool TryParse(string? text, string culture, out decimal amount)
    {
        if (!Formats.TryGetValue(culture, out var format))
        {
            throw new ArgumentException($"'{culture}' is not a supported culture.", nameof(culture));
        }

        if (string.IsNullOrWhiteSpace(text))
        {
            amount = 0m;
            return false;
        }

        return decimal.TryParse(text.Trim(), Styles, format, out amount);
    }

    private static NumberFormatInfo Format(string decimalSeparator, string groupSeparator, string currencySymbol)
    {
        var format = new NumberFormatInfo
        {
            NumberDecimalSeparator = decimalSeparator,
            NumberGroupSeparator = groupSeparator,
            CurrencyDecimalSeparator = decimalSeparator,
            CurrencyGroupSeparator = groupSeparator,
            CurrencySymbol = currencySymbol,
            NegativeSign = "-",
            PositiveSign = "+",
        };

        return NumberFormatInfo.ReadOnly(format);
    }
}

/// <summary>
/// Text to <see cref="DateOnly"/>, under the exact format the template declared. A
/// wrong format fails on row one; it never swaps day and month.
/// </summary>
public static class DateParser
{
    /// <summary>
    /// A format has to name the day, the month and the year. <c>dd/MM</c> would parse
    /// with the current year filled in, which is a guess and therefore not allowed.
    /// </summary>
    public static bool IsValidFormat(string? format) =>
        !string.IsNullOrWhiteSpace(format)
        && format.Contains('d')
        && format.Contains('M')
        && format.Contains('y');

    /// <summary>
    /// <see cref="DateOnly.TryParseExact(string, string, IFormatProvider, DateTimeStyles, out DateOnly)"/>
    /// with the invariant culture and no styles: no inference, no rollover, no
    /// tolerance for a missing zero the format asked for. Surrounding whitespace is
    /// the one thing trimmed, because a cell padded by a spreadsheet is not a
    /// different date.
    /// </summary>
    public static bool TryParseExact(string? text, string format, out DateOnly date)
    {
        date = default;

        if (string.IsNullOrWhiteSpace(text) || !IsValidFormat(format))
        {
            return false;
        }

        return DateOnly.TryParseExact(
            text.Trim(),
            format,
            CultureInfo.InvariantCulture,
            DateTimeStyles.None,
            out date);
    }
}
