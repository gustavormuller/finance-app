using System.Text;
using Finance.Api.Domain.Import;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// Spec unit tests 25-28, and the shapes real exports take: Inter's preamble,
/// Nubank's commas, a trailing delimiter, Latin-1 bytes.
/// </summary>
public sealed class CsvStatementParserTests
{
    /// <summary>Spec unit test 25.</summary>
    [Fact]
    public void A_quoted_field_may_contain_the_delimiter()
    {
        var table = CsvStatementParser.Parse(
            "Data;Descrição;Valor\n10/09/2026;\"Compra; parcelada 1/3\";-42,90\n",
            ';',
            hasHeader: true);

        var record = Assert.Single(table.Records);

        Assert.Null(record.Issue);
        Assert.Equal(["10/09/2026", "Compra; parcelada 1/3", "-42,90"], record.Fields);
    }

    /// <summary>Spec unit test 26.</summary>
    [Fact]
    public void A_quoted_field_may_contain_a_newline()
    {
        var table = CsvStatementParser.Parse(
            "Data,Descrição,Valor\n10/09/2026,\"Pix enviado\nJOÃO\",-42.90\n11/09/2026,Depois,1.00\n",
            ',',
            hasHeader: true);

        Assert.Equal(2, table.Records.Count);
        Assert.Equal("Pix enviado\nJOÃO", table.Records[0].Fields[1]);
        Assert.Equal("Depois", table.Records[1].Fields[1]);
    }

    /// <summary>Spec unit test 27, both ways.</summary>
    [Fact]
    public void The_header_row_is_skipped_only_when_declared()
    {
        const string text = "Data;Valor\n10/09/2026;-1,00\n";

        var withHeader = CsvStatementParser.Parse(text, ';', hasHeader: true);
        var without = CsvStatementParser.Parse(text, ';', hasHeader: false);

        Assert.Equal(["Data", "Valor"], withHeader.Headers);
        Assert.Single(withHeader.Records);

        Assert.Null(without.Headers);
        Assert.Equal(2, without.Records.Count);
        Assert.Equal("Data", without.Records[0].Fields[0]);
    }

    /// <summary>Spec unit test 28.</summary>
    [Fact]
    public void A_ragged_row_is_marked_and_the_rest_of_the_file_still_parses()
    {
        var table = CsvStatementParser.Parse(
            "Data;Descrição;Valor\n10/09/2026;Ok;-1,00\n11/09/2026;Faltou o valor\n12/09/2026;Ok;-2,00;extra;campos\n13/09/2026;Ok;-3,00\n",
            ';',
            hasHeader: true);

        Assert.Equal(4, table.Records.Count);
        Assert.Null(table.Records[0].Issue);
        Assert.Equal(RowIssues.RaggedRow(2, 3), table.Records[1].Issue);
        Assert.Equal(RowIssues.RaggedRow(5, 3), table.Records[2].Issue);
        Assert.Null(table.Records[3].Issue);

        // Row numbers are file lines, so the message points at what the user sees.
        Assert.Equal(3, table.Records[1].RowNumber);
        Assert.Equal(5, table.Records[3].RowNumber);
    }

    /// <summary>
    /// Banco Inter: four lines of account, period and balance above the header, a
    /// semicolon delimiter, and a Saldo column beside Valor.
    /// </summary>
    [Fact]
    public void Preamble_lines_above_the_header_are_skipped()
    {
        const string inter =
            "Extrato Conta Corrente\n"
            + "Conta ;72385499\n"
            + "Período ;19/08/2026 a 17/09/2026\n"
            + "Saldo ;1.992,51\n"
            + "\n"
            + "Data Lançamento;Histórico;Descrição;Valor;Saldo\n"
            + "30/08/2026;Pix enviado;JOAO DA SILVA;-250,00;1.742,51\n"
            + "30/08/2026;Pix enviado;JOAO DA SILVA;-250,00;1.492,51\n"
            + "02/09/2026;Pagamento efetuado;NETFLIX.COM;-55,90;1.436,61\n";

        var table = CsvStatementParser.Parse(inter, ';', hasHeader: true);

        Assert.Equal(4, table.SkippedRows);
        Assert.Equal(["Data Lançamento", "Histórico", "Descrição", "Valor", "Saldo"], table.Headers);
        Assert.Equal(3, table.Records.Count);
        Assert.All(table.Records, record => Assert.Null(record.Issue));
        Assert.Equal("-55,90", table.Records[2].Fields[3]);
    }

    [Fact]
    public void A_trailing_delimiter_on_data_lines_is_trimmed_to_the_header_width()
    {
        var table = CsvStatementParser.Parse(
            "Data;Descrição;Valor\n10/09/2026;Ok;-1,00;\n11/09/2026;Ok;-2,00;\n",
            ';',
            hasHeader: true);

        Assert.Equal(3, table.ColumnCount);
        Assert.All(table.Records, record =>
        {
            Assert.Null(record.Issue);
            Assert.Equal(3, record.Fields.Count);
        });
    }

    /// <summary>Bradesco ends every line, header included, with a delimiter.</summary>
    [Fact]
    public void A_trailing_delimiter_on_every_line_does_not_invent_a_column()
    {
        var table = CsvStatementParser.Parse(
            "Extrato de: Ag: 1234 | Conta: 0012345-6\n"
            + "Data;Histórico;Docto.;Crédito (R$);Débito (R$);Saldo (R$);\n"
            + "05/08/26;TED RECEBIDA;1234;2.000,00;;3.234,56;\n"
            + "06/08/26;PAGTO COBRANCA;5566;;-189,45;3.045,11;\n",
            ';',
            hasHeader: true);

        Assert.Equal(1, table.SkippedRows);
        Assert.Equal(6, table.ColumnCount);
        Assert.Equal(["Data", "Histórico", "Docto.", "Crédito (R$)", "Débito (R$)", "Saldo (R$)"], table.Headers);
        Assert.Equal(2, table.Records.Count);
        Assert.All(table.Records, record => Assert.Equal(6, record.Fields.Count));
        Assert.Equal("-189,45", table.Records[1].Fields[4]);
    }

    [Fact]
    public void An_empty_last_column_is_not_a_trailing_delimiter()
    {
        // The Débito/Crédito layout with an empty Crédito as the last column.
        var table = CsvStatementParser.Parse(
            "Data;Lançamento;Débito;Crédito\n10/09/2026;Conta de luz;150,00;\n11/09/2026;Salário;;3000,00\n",
            ';',
            hasHeader: true);

        Assert.Equal(4, table.ColumnCount);
        Assert.Equal(["10/09/2026", "Conta de luz", "150,00", ""], table.Records[0].Fields);
        Assert.Equal(["11/09/2026", "Salário", "", "3000,00"], table.Records[1].Fields);
    }

    [Fact]
    public void Blank_lines_and_delimiter_only_lines_are_ignored()
    {
        var table = CsvStatementParser.Parse(
            "Data;Valor\n\n10/09/2026;-1,00\n;\n\n11/09/2026;-2,00\n;;;\n",
            ';',
            hasHeader: true);

        Assert.Equal(2, table.Records.Count);
    }

    [Fact]
    public void Windows_line_endings_and_escaped_quotes_are_handled()
    {
        var table = CsvStatementParser.Parse(
            "Data,Descrição,Valor\r\n10/09/2026,\"Loja \"\"Central\"\"\",-1.00\r\n",
            ',',
            hasHeader: true);

        Assert.Equal("Loja \"Central\"", Assert.Single(table.Records).Fields[1]);
    }

    [Fact]
    public void A_stray_quote_spoils_one_field_not_the_file()
    {
        var table = CsvStatementParser.Parse(
            "Data;Descrição;Valor\n10/09/2026;Loja 5\" de tela;-1,00\n11/09/2026;Ok;-2,00\n",
            ';',
            hasHeader: true);

        Assert.Equal(2, table.Records.Count);
        Assert.Equal("Ok", table.Records[1].Fields[1]);
    }

    [Theory]
    [InlineData("")]
    [InlineData("\n\n")]
    [InlineData(";;\n;;\n")]
    public void Nothing_parses_to_an_empty_table(string text)
    {
        var table = CsvStatementParser.Parse(text, ';', hasHeader: true);

        Assert.Empty(table.Records);
        Assert.Null(table.Headers);
    }

    [Theory]
    [InlineData("Data,Valor,Identificador,Descrição\n24/08/2026,-58.00,66c1-abc,Compra no débito - Uber\n", ',')]
    [InlineData("Data Lançamento;Histórico;Descrição;Valor;Saldo\n30/08/2026;Pix enviado;JOAO;-250,00;1.742,51\n", ';')]
    [InlineData("date,category,title,amount\n2026-08-24,transporte,Uber,58.00\n", ',')]
    [InlineData("Data\tDescrição\tValor\n10/09/2026\tLoja\t-1,00\n", '\t')]
    [InlineData("Data|Descrição|Valor\n10/09/2026|Loja, centro|-1,00\n", '|')]
    [InlineData("", ';')]
    public void The_delimiter_is_detected_from_the_first_lines(string text, char expected) =>
        Assert.Equal(expected, CsvStatementParser.DetectDelimiter(text));

    [Fact]
    public void Latin1_bytes_decode_with_their_accents_intact()
    {
        var bytes = Encoding.Latin1.GetBytes("Data;Descrição\n10/09/2026;Alimentação\n");

        var table = CsvStatementParser.Parse(StatementText.Decode(bytes), ';', hasHeader: true);

        Assert.Equal("Alimentação", Assert.Single(table.Records).Fields[1]);
    }

    [Fact]
    public void Utf8_with_and_without_a_bom_decodes_the_same()
    {
        const string text = "Data;Descrição\n10/09/2026;Alimentação\n";
        var plain = Encoding.UTF8.GetBytes(text);
        var withBom = Encoding.UTF8.GetPreamble().Concat(plain).ToArray();

        Assert.Equal(text, StatementText.Decode(plain));
        Assert.Equal(text, StatementText.Decode(withBom));
        Assert.Equal(text, StatementText.Decode(Encoding.Unicode.GetPreamble().Concat(Encoding.Unicode.GetBytes(text)).ToArray()));
    }

    /// <summary>The upload limit is 5 000 rows; parsing them must stay well inside a request.</summary>
    [Fact]
    public void Five_thousand_rows_parse_quickly()
    {
        var builder = new StringBuilder("Data;Descrição;Valor\n");

        for (var row = 0; row < 5_000; row++)
        {
            builder.Append("10/09/2026;\"Compra; número ").Append(row).Append("\";-42,90\n");
        }

        var started = System.Diagnostics.Stopwatch.StartNew();
        var table = CsvStatementParser.Parse(builder.ToString(), ';', hasHeader: true);

        Assert.Equal(5_000, table.Records.Count);
        Assert.True(started.ElapsedMilliseconds < 2_000, $"took {started.ElapsedMilliseconds} ms");
    }
}
