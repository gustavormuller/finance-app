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
public static class OfxParser
{
    public const string NotOfx = "O arquivo não é um extrato OFX: a tag <OFX> não foi encontrada.";

    public static OfxParseResult Parse(string text) => throw new NotImplementedException();
}
