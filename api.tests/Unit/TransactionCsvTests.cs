using System.Globalization;
using System.Reflection;
using Finance.Api.Domain.Transactions;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// Spec 021 unit tests 1 to 6: how one transaction is written into the export, for a
/// Brazilian Excel.
/// </summary>
/// <remarks>
/// Amounts are written as strings and parsed, as in <see cref="MoneyTests"/>: an
/// attribute argument cannot be a <see cref="decimal"/>.
/// </remarks>
public sealed class TransactionCsvTests
{
    /// <summary>Spec 021 unit test 1.</summary>
    [Theory]
    [InlineData("-1234.56", "-1234,56")]
    [InlineData("1234567890.12", "1234567890,12")]
    [InlineData("-0.01", "-0,01")]
    [InlineData("3000", "3000,00")]
    [InlineData("42.9", "42,90")]
    public void Amounts_have_a_decimal_comma_two_places_and_no_grouping(string amount, string expected) =>
        Assert.Equal(expected, TransactionCsv.Amount(decimal.Parse(amount, CultureInfo.InvariantCulture)));

    /// <summary>Spec 021 unit test 1, the declared-type half.</summary>
    [Fact]
    public void Nothing_on_the_writer_is_declared_as_a_floating_point_type()
    {
        Type[] floating = [typeof(double), typeof(float), typeof(Half)];

        var declared = typeof(TransactionCsv).GetMethods(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static)
            .SelectMany(method => method.GetParameters().Select(parameter => parameter.ParameterType).Append(method.ReturnType))
            .Concat(typeof(ExportedTransaction).GetProperties().Select(property => property.PropertyType))
            .Select(type => Nullable.GetUnderlyingType(type) ?? type);

        Assert.DoesNotContain(declared, floating.Contains);
    }

    /// <summary>Spec 021 unit test 2.</summary>
    [Fact]
    public void Dates_are_day_month_year() =>
        Assert.Equal("03/04/2026", TransactionCsv.Date(new DateOnly(2026, 4, 3)));

    /// <summary>Spec 021 unit test 3.</summary>
    [Theory]
    [InlineData("Supermercado", "Supermercado")]
    [InlineData("Pão de Açúcar", "Pão de Açúcar")]
    [InlineData("Mercado; feira", "\"Mercado; feira\"")]
    [InlineData("Loja \"Top\"", "\"Loja \"\"Top\"\"\"")]
    [InlineData("Linha 1\nLinha 2", "\"Linha 1\nLinha 2\"")]
    [InlineData("Linha 1\r\nLinha 2", "\"Linha 1\r\nLinha 2\"")]
    [InlineData("vírgula, não separa", "vírgula, não separa")]
    [InlineData("", "")]
    public void Text_is_quoted_only_when_it_holds_the_separator_a_quote_or_a_line_break(string text, string expected) =>
        Assert.Equal(expected, TransactionCsv.Text(text));

    /// <summary>Spec 021 unit test 4.</summary>
    [Theory]
    [InlineData("=1+1", "'=1+1")]
    [InlineData("+55 11 9999", "'+55 11 9999")]
    [InlineData("-Estorno", "'-Estorno")]
    [InlineData("@SOMA(A1)", "'@SOMA(A1)")]
    [InlineData("\t=1+1", "'\t=1+1")]
    [InlineData("\r=1+1", "\"'\r=1+1\"")]
    [InlineData("=HYPERLINK(\"x\";\"y\")", "\"'=HYPERLINK(\"\"x\"\";\"\"y\"\")\"")]
    [InlineData("Pix - João", "Pix - João")]
    [InlineData("1+1=2", "1+1=2")]
    public void Text_that_a_spreadsheet_would_run_as_a_formula_gets_an_apostrophe(string text, string expected) =>
        Assert.Equal(expected, TransactionCsv.Text(text));

    /// <summary>Spec 021 unit test 5.</summary>
    [Fact]
    public void A_record_has_the_seven_columns_in_the_headers_order()
    {
        Assert.Equal("Data;Descrição;Valor;Moeda;Conta;Categoria;Subcategoria", TransactionCsv.Header);

        var onChild = new ExportedTransaction(
            new DateOnly(2026, 9, 30), "Mercado; \"Pão\"", -1234.56m, "BRL", "Nubank", "Alimentação", "Supermercado");
        var onMain = new ExportedTransaction(
            new DateOnly(2026, 9, 28), "-Estorno", -0.01m, "BRL", "Nubank", "Outros", null);

        Assert.Equal(
            "30/09/2026;\"Mercado; \"\"Pão\"\"\";-1234,56;BRL;Nubank;Alimentação;Supermercado",
            TransactionCsv.Record(onChild));

        // The amount's minus is the app's own, so it stays a number; the description's is not.
        Assert.Equal("28/09/2026;'-Estorno;-0,01;BRL;Nubank;Outros;", TransactionCsv.Record(onMain));
    }

    /// <summary>Spec 021 unit test 6.</summary>
    [Fact]
    public void The_file_is_named_after_the_range_it_holds()
    {
        var first = new DateOnly(2026, 9, 1);
        var last = new DateOnly(2026, 9, 30);

        Assert.Equal("lancamentos-2026-09-01-a-2026-09-30.csv", TransactionCsv.FileName(first, last));
        Assert.Equal("lancamentos-desde-2026-09-01.csv", TransactionCsv.FileName(first, null));
        Assert.Equal("lancamentos-ate-2026-09-30.csv", TransactionCsv.FileName(null, last));
        Assert.Equal("lancamentos.csv", TransactionCsv.FileName(null, null));
    }
}
