using System.Buffers;
using System.Globalization;

namespace Finance.Api.Domain.Transactions;

/// <summary>One transaction as the export writes it.</summary>
/// <param name="Category">Always the main category: the parent, for a transaction on a child.</param>
/// <param name="Subcategory">The child the transaction is on; null for one on a main category.</param>
public sealed record ExportedTransaction(
    DateOnly Date,
    string Description,
    decimal Amount,
    string Currency,
    string Account,
    string Category,
    string? Subcategory);

/// <summary>
/// The transactions export (spec 021) as a CSV a Brazilian Excel opens as a table: <c>;</c>
/// between fields, dates as <c>dd/MM/yyyy</c> and amounts with a decimal comma.
/// </summary>
/// <remarks>
/// Hand-written rather than CsvHelper's writer: writing RFC 4180 is the easy half of CSV,
/// and CsvHelper's injection escaping applies to every field, so it would turn the
/// negative amounts into text.
/// </remarks>
public static class TransactionCsv
{
    public const char Separator = ';';

    /// <summary>After every record, the last one included (RFC 4180).</summary>
    public const string LineBreak = "\r\n";

    public const string Header = "Data;Descrição;Valor;Moeda;Conta;Categoria;Subcategoria";

    /// <summary>What Excel reads as the start of a formula (OWASP, "CSV Injection").</summary>
    private static readonly SearchValues<char> FormulaStarts = SearchValues.Create("=+-@\t\r");

    private static readonly SearchValues<char> QuotedWhenPresent = SearchValues.Create(";\"\r\n");

    /// <summary>
    /// The invariant rules with a comma for the decimals. The format string has no
    /// grouping, so <c>-1234,56</c> is a number to Excel pt-BR, not text.
    /// </summary>
    private static readonly NumberFormatInfo DecimalComma =
        NumberFormatInfo.ReadOnly(new NumberFormatInfo { NumberDecimalSeparator = ",", NegativeSign = "-" });

    public static string Record(ExportedTransaction transaction) =>
        string.Join(
            Separator,
            Date(transaction.Date),
            Text(transaction.Description),
            Amount(transaction.Amount),
            Text(transaction.Currency),
            Text(transaction.Account),
            Text(transaction.Category),
            Text(transaction.Subcategory ?? string.Empty));

    public static string Amount(decimal amount) => amount.ToString("0.00", DecimalComma);

    public static string Date(DateOnly date) => date.ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

    /// <summary>
    /// A field a person typed. One that a spreadsheet would run as a formula gets a
    /// leading <c>'</c>, which Excel shows and never evaluates; then it is quoted if it
    /// holds the separator, a quote or a line break.
    /// </summary>
    public static string Text(string value)
    {
        var guarded = value.Length > 0 && FormulaStarts.Contains(value[0]) ? "'" + value : value;

        return guarded.AsSpan().ContainsAny(QuotedWhenPresent)
            ? "\"" + guarded.Replace("\"", "\"\"", StringComparison.Ordinal) + "\""
            : guarded;
    }

    /// <summary>ISO dates, so a folder of exports sorts by period; ASCII, so the header needs no encoding.</summary>
    public static string FileName(DateOnly? from, DateOnly? to) => (from, to) switch
    {
        ({ } start, { } end) => $"lancamentos-{Iso(start)}-a-{Iso(end)}.csv",
        ({ } start, null) => $"lancamentos-desde-{Iso(start)}.csv",
        (null, { } end) => $"lancamentos-ate-{Iso(end)}.csv",
        _ => "lancamentos.csv",
    };

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
}
