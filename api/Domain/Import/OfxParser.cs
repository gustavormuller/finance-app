using System.Globalization;
using System.Net;

namespace Finance.Api.Domain.Import;

/// <summary>What an OFX file yielded: its currency and one row per <c>STMTTRN</c>.</summary>
public sealed record OfxStatement(string? Currency, IReadOnlyList<ParsedRow> Rows);

/// <summary>
/// Either a statement or a pt-BR reason there is none. A file that is not OFX at all
/// is an error; a file that is OFX with broken rows is a statement whose rows carry
/// issues, because three bad rows out of two hundred must not reject the other
/// hundred and ninety-seven.
/// </summary>
public sealed record OfxParseResult(OfxStatement? Statement, string? Error)
{
    public static OfxParseResult Ok(OfxStatement statement) => new(statement, null);

    public static OfxParseResult Failed(string error) => new(null, error);
}

/// <summary>
/// A tolerant tag scanner for the <c>STMTTRN</c> subset of OFX (spec decision 13).
/// </summary>
/// <remarks>
/// OFX 1.x — what Brazilian banks export — is SGML with unclosed tags, so no XML
/// parser reads it. The rule that makes it tractable: a leaf's value runs from the
/// end of its opening tag to the next <c>&lt;</c>, whether or not a closing tag
/// follows. That reads 1.x and 2.x alike, and survives a bank that closes some tags
/// and not others.
/// <para>
/// Tolerances, each of which a real file has needed: tag names matched
/// case-insensitively; SGML entities decoded; a <c>TRNAMT</c> written with a
/// decimal comma read as such (the string decides, never the magnitude); the
/// calendar day taken from the first eight digits of <c>DTPOSTED</c> and the
/// timezone suffix ignored, because the bank's local day is the day the user sees
/// on their own screen; <c>NAME</c> and <c>MEMO</c> that repeat each other kept
/// once; a per-transaction <c>CURSYM</c> overriding the statement's <c>CURDEF</c>.
/// </para>
/// </remarks>
public static class OfxParser
{
    public const string NotOfx = "O arquivo não é um extrato OFX: a tag <OFX> não foi encontrada.";

    private const string DescriptionSeparator = CsvMapping.DescriptionSeparator;

    private const StringComparison Tag = StringComparison.OrdinalIgnoreCase;

    private static readonly NumberFormatInfo DecimalPoint = NumberFormatInfo.InvariantInfo;

    private static readonly NumberFormatInfo DecimalComma = new()
    {
        NumberDecimalSeparator = ",",
        NumberGroupSeparator = ".",
        NegativeSign = "-",
        PositiveSign = "+",
    };

    public static OfxParseResult Parse(string text)
    {
        if (string.IsNullOrWhiteSpace(text) || text.IndexOf("<OFX>", Tag) < 0)
        {
            return OfxParseResult.Failed(NotOfx);
        }

        var currency = Normalize(Leaf(text, "CURDEF", 0, text.Length));
        var rows = new List<ParsedRow>();

        var start = text.IndexOf("<STMTTRN>", Tag);

        while (start >= 0)
        {
            var next = text.IndexOf("<STMTTRN>", start + 1, Tag);
            var close = text.IndexOf("</STMTTRN>", start, Tag);

            // The block ends at its closing tag, or at the next block when a bank
            // forgot to close it, or at the end of the file.
            var end = close >= 0 && (next < 0 || close < next)
                ? close
                : next >= 0 ? next : text.Length;

            rows.Add(ReadTransaction(text, start, end, rows.Count + 1, currency));
            start = next;
        }

        return OfxParseResult.Ok(new OfxStatement(currency, rows));
    }

    private static ParsedRow ReadTransaction(string text, int start, int end, int rowNumber, string? statementCurrency)
    {
        var issues = new List<string>();

        var postedText = Leaf(text, "DTPOSTED", start, end) ?? "";
        DateOnly? date = ReadDay(postedText, out var day) ? day : null;

        if (date is null)
        {
            issues.Add(RowIssues.InvalidDate(postedText));
        }

        var amountText = Leaf(text, "TRNAMT", start, end) ?? "";
        decimal? amount = ReadAmount(amountText, out var value) ? value : null;

        if (amount is null)
        {
            issues.Add(RowIssues.InvalidAmount(amountText));
        }

        var name = Leaf(text, "NAME", start, end);
        var memo = Leaf(text, "MEMO", start, end);
        var externalId = Leaf(text, "FITID", start, end);
        var currency = Normalize(Leaf(text, "CURSYM", start, end)) ?? statementCurrency;

        return new ParsedRow(rowNumber, date, amount, currency, Describe(name, memo), externalId, issues);
    }

    /// <summary>
    /// The value of the first <c>&lt;tag&gt;</c> between <paramref name="start"/>
    /// and <paramref name="end"/>: everything up to the next <c>&lt;</c>, trimmed
    /// and entity-decoded. Null when the tag is absent or its value is empty.
    /// </summary>
    private static string? Leaf(string text, string tag, int start, int end)
    {
        var open = "<" + tag + ">";
        var at = text.IndexOf(open, start, end - start, Tag);

        if (at < 0)
        {
            return null;
        }

        var valueStart = at + open.Length;
        var valueEnd = text.IndexOf('<', valueStart);

        if (valueEnd < 0 || valueEnd > end)
        {
            valueEnd = end;
        }

        var value = WebUtility.HtmlDecode(text[valueStart..valueEnd]).Trim();

        return value.Length == 0 ? null : value;
    }

    /// <summary>
    /// <c>YYYYMMDD</c>, optionally followed by a time, fractional seconds and a
    /// timezone in brackets. The first eight characters are the bank's local day
    /// and are what the user's own statement shows (spec unit test 24).
    /// </summary>
    private static bool ReadDay(string posted, out DateOnly day)
    {
        day = default;

        return posted.Length >= 8
            && posted.Take(8).All(char.IsAsciiDigit)
            && DateParser.TryParseExact(posted[..8], "yyyyMMdd", out day);
    }

    /// <summary>
    /// Signed, as the file wrote it. The specification says decimal point; some
    /// Brazilian banks write a decimal comma. Whichever separator comes last in
    /// the text is the decimal one — a property of the string, so the same text
    /// always reads the same way.
    /// </summary>
    private static bool ReadAmount(string amountText, out decimal amount)
    {
        var format = amountText.LastIndexOf(',') > amountText.LastIndexOf('.') ? DecimalComma : DecimalPoint;

        return decimal.TryParse(amountText, NumberStyles.Number, format, out amount);
    }

    private static string Describe(string? name, string? memo) =>
        (name, memo) switch
        {
            (null, null) => "",
            (var only, null) => only,
            (null, var only) => only,
            var (first, second) when string.Equals(first, second, StringComparison.Ordinal) => first,
            var (first, second) => first + DescriptionSeparator + second,
        };

    private static string? Normalize(string? currency) => currency?.ToUpperInvariant();
}
