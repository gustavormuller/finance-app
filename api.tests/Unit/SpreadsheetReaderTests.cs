using System.IO.Compression;
using System.Text;
using System.Text.RegularExpressions;
using Finance.Api.Domain.Import;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// Spec 011 unit tests 1–10. The fixtures are built by
/// <c>samples/statements/generate-spreadsheets.py</c>: <c>typed.*</c> holds real date and
/// number cells under two preamble lines and around a blank one, <c>text.xlsx</c> holds
/// the same kind of values as text.
/// </summary>
public sealed partial class SpreadsheetReaderTests
{
    private const string PtBr = "pt-BR";
    private const string EnUs = "en-US";

    /// <summary>Spec test 1.</summary>
    [Fact]
    public void Typed_cells_are_written_in_the_declared_culture_and_date_format()
    {
        var table = Read("typed.xlsx");

        Assert.Equal(["Data", "Descrição", "Valor"], table.Headers);
        Assert.Equal(
            [["03/08/2026", "Uber", "-58"], ["05/08/2026", "Mercado", "1234,56"], ["10/08/2026", "Rendimento", "0,3"]],
            table.Records.Select(record => record.Fields));
    }

    /// <summary>Spec test 2: a typed cell means the same thing under any declared format.</summary>
    [Fact]
    public void Typed_cells_round_trip_under_either_culture()
    {
        var brazilian = Interpret(Read("typed.xlsx"), PtBr, "dd/MM/yyyy");
        var american = Interpret(Read("typed.xlsx", EnUs, "MM/dd/yyyy"), EnUs, "MM/dd/yyyy");

        Assert.All(brazilian, row => Assert.Empty(row.Issues));
        Assert.Equal(brazilian.Select(Values), american.Select(Values));
    }

    /// <summary>Spec test 3, including the 0.1 + 0.2 Excel really stores.</summary>
    [Fact]
    public void Typed_numbers_convert_to_exact_decimals()
    {
        var rows = Interpret(Read("typed.xlsx"), PtBr, "dd/MM/yyyy");

        Assert.Equal([-58m, 1234.56m, 0.3m], rows.Select(row => row.Amount));
        Assert.Equal(
            [new DateOnly(2026, 8, 3), new DateOnly(2026, 8, 5), new DateOnly(2026, 8, 10)],
            rows.Select(row => row.Date));
    }

    /// <summary>Spec test 4: a text cell is a CSV field, with the CSV's rules.</summary>
    [Fact]
    public void Text_cells_are_parsed_like_csv_fields()
    {
        Assert.Equal(["05/08/2026", "Loja", "1.234,56"], Read("text.xlsx").Records.Single().Fields);

        Assert.Equal(1234.56m, Interpret(Read("text.xlsx"), PtBr, "dd/MM/yyyy").Single().Amount);

        var american = Interpret(Read("text.xlsx", EnUs), EnUs, "dd/MM/yyyy").Single();
        Assert.Null(american.Amount);
        Assert.Contains(RowIssues.InvalidAmount("1.234,56"), american.Issues);
    }

    /// <summary>Spec test 5. Row numbers are Excel's.</summary>
    [Fact]
    public void Preamble_is_skipped_and_counted_and_blank_rows_are_ignored()
    {
        var table = Read("typed.xlsx");

        Assert.Equal(2, table.SkippedRows);
        Assert.Equal([5, 6, 8], table.Records.Select(record => record.RowNumber));
    }

    /// <summary>Spec test 6.</summary>
    [Fact]
    public void Xls_yields_the_same_records_as_xlsx()
    {
        var xlsx = Read("typed.xlsx");
        var xls = Read("typed.xls");

        Assert.Equal(xlsx.Headers, xls.Headers);
        Assert.Equal(
            xlsx.Records.Select(record => (record.RowNumber, string.Join("|", record.Fields))),
            xls.Records.Select(record => (record.RowNumber, string.Join("|", record.Fields))));
    }

    /// <summary>Spec test 7.</summary>
    [Fact]
    public void An_empty_sheet_falls_through_to_the_next_and_an_empty_workbook_is_an_empty_table()
    {
        Assert.Equal(["Data", "Descrição", "Valor"], Read("second-sheet.xlsx").Headers);
        Assert.Equal(2, Read("second-sheet.xlsx").Records.Count);

        Assert.Empty(Read("empty.xlsx").Records);
    }

    /// <summary>Spec test 8. HTML is what some banks save as <c>.xls</c>.</summary>
    [Theory]
    [InlineData("<html><body><table><tr><td>Data</td><td>Valor</td></tr></table></body></html>")]
    [InlineData("Data;Descrição;Valor\n05/08/2026;Loja;-10,00\n")]
    public void Text_is_not_a_spreadsheet(string content)
    {
        var bytes = Encoding.UTF8.GetBytes(content);

        Assert.False(SpreadsheetStatementReader.IsSpreadsheet(bytes));
        Assert.Equal(SpreadsheetProblem.NotASpreadsheet, SpreadsheetStatementReader.Read(bytes, PtBr, "dd/MM/yyyy", hasHeader: true).Problem);
    }

    /// <summary>Spec test 8: the right signature on a broken body is still refused, not thrown.</summary>
    [Theory]
    [InlineData(new byte[] { 0x50, 0x4B, 0x03, 0x04 })]
    [InlineData(new byte[] { 0xD0, 0xCF, 0x11, 0xE0, 0xA1, 0xB1, 0x1A, 0xE1 })]
    public void A_corrupt_body_behind_a_valid_signature_is_not_a_spreadsheet(byte[] signature)
    {
        var bytes = signature.Concat(Enumerable.Repeat((byte)0x2A, 600)).ToArray();

        Assert.True(SpreadsheetStatementReader.IsSpreadsheet(bytes));
        Assert.Equal(SpreadsheetProblem.NotASpreadsheet, SpreadsheetStatementReader.Read(bytes, PtBr, "dd/MM/yyyy", hasHeader: true).Problem);
    }

    /// <summary>Spec test 9: a few kilobytes of ZIP that declare 21 MB.</summary>
    [Fact]
    public void A_zip_declaring_more_than_the_limit_uncompressed_is_refused()
    {
        using var buffer = new MemoryStream();
        using (var zip = new ZipArchive(buffer, ZipArchiveMode.Create, leaveOpen: true))
        {
            using var entry = zip.CreateEntry("xl/sharedStrings.xml", CompressionLevel.SmallestSize).Open();
            var chunk = new byte[1024 * 1024];
            for (var i = 0; i < 21; i++)
            {
                entry.Write(chunk);
            }
        }

        var bytes = buffer.ToArray();

        Assert.True(bytes.Length < 200 * 1024);
        Assert.Equal(SpreadsheetProblem.TooLarge, SpreadsheetStatementReader.Read(bytes, PtBr, "dd/MM/yyyy", hasHeader: true).Problem);
    }

    /// <summary>Spec test 10: no binary floating point in our code on this path.</summary>
    [Fact]
    public void The_reader_holds_no_double_or_float()
    {
        var file = Path.Combine(TestPaths.RepositoryRoot(), "api", "Domain", "Import", "SpreadsheetStatementReader.cs");

        var offenders = File.ReadLines(file)
            .Select((line, number) => (number, code: Comment().Replace(line, "")))
            .Where(line => FloatingPoint().IsMatch(line.code))
            .Select(line => $"{line.number + 1}: {line.code.Trim()}");

        Assert.Empty(offenders);
    }

    private static CsvTable Read(string fixture, string culture = PtBr, string dateFormat = "dd/MM/yyyy")
    {
        var bytes = File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Spreadsheets", fixture));
        var result = SpreadsheetStatementReader.Read(bytes, culture, dateFormat, hasHeader: true);

        Assert.Null(result.Problem);
        return result.Table!;
    }

    private static IReadOnlyList<ParsedRow> Interpret(CsvTable table, string culture, string dateFormat) =>
        CsvRowInterpreter.Interpret(
            table,
            new CsvMapping(';', true, culture, dateFormat, SignMode.Signed, "Data", "Valor", null, null, "Descrição"));

    private static (int, DateOnly?, decimal?, string) Values(ParsedRow row) =>
        (row.RowNumber, row.Date, row.Amount, row.RawDescription);

    [GeneratedRegex(@"//.*$")]
    private static partial Regex Comment();

    [GeneratedRegex(@"\b(double|float|Half|Single|Double)\b|\d(d|f|D|F)\b")]
    private static partial Regex FloatingPoint();
}
