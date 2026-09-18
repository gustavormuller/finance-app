namespace Finance.Api.Domain.Import;

/// <summary>
/// Text to <see cref="decimal"/>, under a format the template declared. Never
/// <see cref="double"/>, never <c>Convert</c>, never a guess at the separators.
/// </summary>
public static class AmountParser
{
    public static IReadOnlyCollection<string> SupportedCultures => throw new NotImplementedException();

    public static bool IsSupportedCulture(string? culture) => throw new NotImplementedException();

    public static bool TryParse(string? text, string culture, out decimal amount) =>
        throw new NotImplementedException();
}

/// <summary>
/// Text to <see cref="DateOnly"/>, under the exact format the template declared. A
/// wrong format fails on row one; it never swaps day and month.
/// </summary>
public static class DateParser
{
    public static bool IsValidFormat(string? format) => throw new NotImplementedException();

    public static bool TryParseExact(string? text, string format, out DateOnly date) =>
        throw new NotImplementedException();
}
