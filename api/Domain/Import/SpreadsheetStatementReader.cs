using System.Globalization;
using System.IO.Compression;
using System.Text;
using ExcelDataReader;
using ExcelDataReader.Exceptions;

namespace Finance.Api.Domain.Import;

/// <summary>Why a spreadsheet could not be read. Each has its own pt-BR message at the endpoint.</summary>
public enum SpreadsheetProblem
{
    NotASpreadsheet,
    PasswordProtected,
    TooLarge,
}

/// <summary>The table, or the reason there is none.</summary>
public sealed record SpreadsheetRead(CsvTable? Table, SpreadsheetProblem? Problem);

/// <summary>
/// An <c>.xlsx</c> or <c>.xls</c> statement to the same <see cref="CsvTable"/> a CSV
/// becomes (spec 011), through <c>ExcelDataReader</c>. Everything after the table —
/// mapping, row interpretation, dedupe — is the CSV path, unchanged.
/// </summary>
/// <remarks>
/// A text cell is used as written. A number cell arrives boxed in the binary floating
/// point Excel stores, and <see cref="Convert.ToDecimal(object, IFormatProvider)"/> turns
/// it into a <see cref="decimal"/> at 15 significant digits: Excel's own precision, so the
/// <c>0.30000000000000004</c> a sum of 0,10 and 0,20 stores becomes <c>0.3</c>. The
/// reader's <c>GetDecimal</c> cannot do this; it unboxes and throws. A date cell becomes
/// a <see cref="DateOnly"/>. Both are then written as text in the culture and date format
/// the mapping declares, which parse back to exactly the value in the sheet (decision 5).
/// </remarks>
public static class SpreadsheetStatementReader
{
    /// <summary>Decision 10: what a 2 MB ZIP may declare once decompressed.</summary>
    public const long MaxUncompressedBytes = 20L * 1024 * 1024;

    private const string DefaultCulture = "pt-BR";
    private const string DefaultDateFormat = "dd/MM/yyyy";

    private static readonly byte[] ZipSignature = [0x50, 0x4B, 0x03, 0x04];
    private static readonly byte[] CompoundFileSignature = [0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1];

    static SpreadsheetStatementReader()
    {
        // BIFF (.xls) strings can carry a Windows code page, which .NET only knows
        // once the provider shipped in the shared framework is registered.
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    /// <summary>The content decides, not the extension (decision 1).</summary>
    public static bool IsSpreadsheet(ReadOnlySpan<byte> bytes) =>
        bytes.StartsWith(ZipSignature) || bytes.StartsWith(CompoundFileSignature);

    /// <summary>
    /// The first worksheet with a non-empty row, as a table. Typed cells are written in
    /// <paramref name="culture"/> and <paramref name="dateFormat"/>; when either is not
    /// valid the defaults are used, and the mapping's own validation reports it.
    /// </summary>
    public static SpreadsheetRead Read(byte[] bytes, string? culture, string? dateFormat, bool hasHeader)
    {
        if (!IsSpreadsheet(bytes))
        {
            return new SpreadsheetRead(null, SpreadsheetProblem.NotASpreadsheet);
        }

        var numberCulture = AmountParser.IsSupportedCulture(culture) ? culture! : DefaultCulture;
        var format = DateParser.IsValidFormat(dateFormat) ? dateFormat! : DefaultDateFormat;

        try
        {
            if (bytes.AsSpan().StartsWith(ZipSignature) && DeclaredUncompressedBytes(bytes) > MaxUncompressedBytes)
            {
                return new SpreadsheetRead(null, SpreadsheetProblem.TooLarge);
            }

            using var stream = new MemoryStream(bytes, writable: false);
            using var reader = ExcelReaderFactory.CreateReader(stream);

            do
            {
                var rows = ReadSheet(reader, numberCulture, format);

                if (rows.Count > 0)
                {
                    return new SpreadsheetRead(CsvStatementParser.FromRows(rows, hasHeader, padShortRows: true), null);
                }
            }
            while (reader.NextResult());

            return new SpreadsheetRead(CsvTable.Empty, null);
        }
        catch (InvalidPasswordException)
        {
            return new SpreadsheetRead(null, SpreadsheetProblem.PasswordProtected);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            // A file with the right signature and a broken body can fail anywhere inside
            // the library. It is the file's problem, reported as such (decision 11).
            return new SpreadsheetRead(null, SpreadsheetProblem.NotASpreadsheet);
        }
    }

    private static long DeclaredUncompressedBytes(byte[] bytes)
    {
        using var zip = new ZipArchive(new MemoryStream(bytes, writable: false), ZipArchiveMode.Read);

        return zip.Entries.Sum(entry => entry.Length);
    }

    private static List<(int RowNumber, string[] Fields)> ReadSheet(IExcelDataReader reader, string culture, string dateFormat)
    {
        var rows = new List<(int, string[])>();
        var rowNumber = 0;

        while (reader.Read())
        {
            rowNumber++;
            var fields = Enumerable.Range(0, reader.FieldCount)
                .Select(column => Cell(reader, column, culture, dateFormat))
                .ToList();

            // Every row of a sheet is as wide as its grid. Dropping the empty cells at the
            // end makes a title line narrow again, the way a CSV writes it.
            while (fields.Count > 0 && string.IsNullOrWhiteSpace(fields[^1]))
            {
                fields.RemoveAt(fields.Count - 1);
            }

            if (fields.Count > 0)
            {
                rows.Add((rowNumber, [.. fields]));
            }
        }

        return rows;
    }

    private static string Cell(IExcelDataReader reader, int column, string culture, string dateFormat) =>
        reader.GetValue(column) switch
        {
            null => "",
            string text => text,
            DateTime moment => DateOnly.FromDateTime(moment).ToString(dateFormat, CultureInfo.InvariantCulture),
            bool flag => flag ? "VERDADEIRO" : "FALSO",
            TimeSpan => "",
            var number => AmountParser.Format(Convert.ToDecimal(number, CultureInfo.InvariantCulture), culture),
        };
}
