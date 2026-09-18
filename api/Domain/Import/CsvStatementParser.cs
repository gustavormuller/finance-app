using System.Globalization;
using CsvHelper;
using CsvHelper.Configuration;

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
/// <remarks>
/// Two things a real statement does that a textbook CSV does not:
/// <list type="bullet">
/// <item>Inter puts four lines of account number, period and balance above the
/// header. The table is taken to start at the first record with the file's modal
/// field count; everything above it is preamble and is counted, not parsed.</item>
/// <item>Some exports end data lines with a delimiter and the header without one.
/// A record with extra <em>empty</em> trailing fields is trimmed to the table's
/// width; a record that is genuinely short or genuinely wide is marked ragged and
/// the rest of the file still parses (spec unit test 28).</item>
/// </list>
/// </remarks>
public static class CsvStatementParser
{
    /// <summary>In order of preference on a tie. Semicolon first: this is Brazil.</summary>
    public static IReadOnlyList<char> CandidateDelimiters { get; } = [';', ',', '\t', '|'];

    private const int LinesSampledForDelimiter = 20;

    private static readonly string[] LineBreaks = ["\r\n", "\n", "\r"];

    /// <summary>
    /// The candidate that occurs most, summed over the first lines of the file.
    /// Deterministic and cheap; the mapping step lets the user override it, and the
    /// preview shows immediately when it was wrong.
    /// </summary>
    public static char DetectDelimiter(string text)
    {
        var lines = text.Split(LineBreaks, StringSplitOptions.RemoveEmptyEntries)
            .Take(LinesSampledForDelimiter)
            .ToList();

        var best = CandidateDelimiters[0];
        var bestCount = -1;

        foreach (var candidate in CandidateDelimiters)
        {
            var count = lines.Sum(line => line.Count(character => character == candidate));

            if (count > bestCount)
            {
                best = candidate;
                bestCount = count;
            }
        }

        return best;
    }

    public static CsvTable Parse(string text, char delimiter, bool hasHeader)
    {
        var records = ReadAll(text, delimiter);

        if (records.Count == 0)
        {
            return CsvTable.Empty;
        }

        var columnCount = ModalFieldCount(records);
        var tableStart = records.FindIndex(record => Fits(record.Fields, columnCount));

        if (tableStart < 0)
        {
            return CsvTable.Empty;
        }

        var headers = hasHeader ? Fit(records[tableStart].Fields, columnCount) : null;
        var firstData = hasHeader ? tableStart + 1 : tableStart;

        var data = records
            .Skip(firstData)
            .Select(record => Fits(record.Fields, columnCount)
                ? new CsvRecord(record.RowNumber, Fit(record.Fields, columnCount), null)
                : new CsvRecord(record.RowNumber, record.Fields, RowIssues.RaggedRow(record.Fields.Length, columnCount)))
            .ToList();

        return new CsvTable(headers, data, columnCount, tableStart);
    }

    private static List<(int RowNumber, string[] Fields)> ReadAll(string text, char delimiter)
    {
        var configuration = new CsvConfiguration(CultureInfo.InvariantCulture)
        {
            Delimiter = delimiter.ToString(),
            HasHeaderRecord = false,
            IgnoreBlankLines = true,

            // A stray quote in a description is that description's problem, not the
            // file's. Left to throw, one bad field would reject 200 good rows.
            BadDataFound = null,
        };

        using var parser = new CsvParser(new StringReader(text), configuration);
        var records = new List<(int, string[])>();

        while (parser.Read())
        {
            var fields = parser.Record ?? [];

            // A line of nothing but delimiters — Excel is fond of saving those below
            // the last row — is a blank line, not a ragged record.
            if (fields.All(string.IsNullOrWhiteSpace))
            {
                continue;
            }

            records.Add((parser.RawRow, fields));
        }

        return records;
    }

    /// <summary>
    /// The width most records agree on. A record with empty trailing fields — a
    /// data line ending in a delimiter — supports every width between its trimmed
    /// and its raw length, so a header without the trailing delimiter still wins.
    /// Records of one field do not vote when anything wider exists: a title line
    /// above a table is not a vote for one-column files. Ties go to the wider table.
    /// </summary>
    private static int ModalFieldCount(List<(int RowNumber, string[] Fields)> records)
    {
        var spans = records
            .Select(record => (Min: TrimmedWidth(record.Fields), Max: record.Fields.Length))
            .ToList();

        var candidates = spans.Select(span => span.Max).Where(width => width > 1).Distinct().ToList();

        if (candidates.Count == 0)
        {
            candidates = spans.Select(span => span.Max).Distinct().ToList();
        }

        return candidates
            .OrderByDescending(width => spans.Count(span => span.Min <= width && width <= span.Max))
            .ThenByDescending(width => width)
            .First();
    }

    private static int TrimmedWidth(string[] fields)
    {
        var width = fields.Length;

        while (width > 1 && string.IsNullOrWhiteSpace(fields[width - 1]))
        {
            width--;
        }

        return width;
    }

    private static bool Fits(string[] fields, int columnCount) =>
        fields.Length == columnCount
        || (fields.Length > columnCount && fields.Skip(columnCount).All(string.IsNullOrWhiteSpace));

    private static string[] Fit(string[] fields, int columnCount) =>
        fields.Length == columnCount ? fields : fields[..columnCount];
}
