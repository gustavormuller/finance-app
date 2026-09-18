namespace Finance.Api.Domain.Import;

/// <summary>One record of the file, or the reason it could not be used as one.</summary>
/// <param name="RowNumber">The line the record ends on, 1-based, as the user's editor counts it.</param>
public sealed record CsvRecord(int RowNumber, IReadOnlyList<string> Fields, string? Issue);

/// <summary>
/// The table inside a CSV export: the header if the caller said there is one, the
/// data records, and how many lines above the table were skipped as preamble.
/// </summary>
public sealed record CsvTable(
    IReadOnlyList<string>? Headers,
    IReadOnlyList<CsvRecord> Records,
    int ColumnCount,
    int SkippedRows)
{
    public static CsvTable Empty { get; } = new(null, [], 0, 0);
}

/// <summary>
/// CSV to records, through CsvHelper (spec decision 13). Quoting, escaping and
/// embedded newlines are its problem; what is added here is what bank exports need
/// on top of RFC 4180.
/// </summary>
public static class CsvStatementParser
{
    /// <summary>In order of preference on a tie. Semicolon first: this is Brazil.</summary>
    public static IReadOnlyList<char> CandidateDelimiters { get; } = [';', ',', '\t', '|'];

    public static char DetectDelimiter(string text) => throw new NotImplementedException();

    public static CsvTable Parse(string text, char delimiter, bool hasHeader) =>
        throw new NotImplementedException();
}
