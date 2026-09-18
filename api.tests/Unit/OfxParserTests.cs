using System.Text;
using Finance.Api.Domain.Import;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// Spec unit tests 18-24, on the SGML that Brazilian banks actually export: a
/// header block, unclosed tags, a timezone on every date, and the quirks each bank
/// adds on top.
/// </summary>
public sealed class OfxParserTests
{
    /// <summary>A minimal 1.x document, the way Itaú, Inter and Nubank write one.</summary>
    private const string Minimal =
        "OFXHEADER:100\nDATA:OFXSGML\nVERSION:102\nSECURITY:NONE\nENCODING:USASCII\nCHARSET:1252\n"
        + "COMPRESSION:NONE\nOLDFILEUID:NONE\nNEWFILEUID:NONE\n\n"
        + "<OFX>\n<SIGNONMSGSRSV1>\n<SONRS>\n<STATUS>\n<CODE>0\n<SEVERITY>INFO\n</STATUS>\n"
        + "<DTSERVER>20260917120000[-3:BRT]\n<LANGUAGE>POR\n</SONRS>\n</SIGNONMSGSRSV1>\n"
        + "<BANKMSGSRSV1>\n<STMTTRNRS>\n<TRNUID>1001\n<STATUS>\n<CODE>0\n<SEVERITY>INFO\n</STATUS>\n"
        + "<STMTRS>\n<CURDEF>BRL\n<BANKACCTFROM>\n<BANKID>0260\n<ACCTID>12345-6\n<ACCTTYPE>CHECKING\n</BANKACCTFROM>\n"
        + "<BANKTRANLIST>\n<DTSTART>20260901000000[-3:BRT]\n<DTEND>20260917000000[-3:BRT]\n"
        + "<STMTTRN>\n<TRNTYPE>DEBIT\n<DTPOSTED>20260902000000[-3:BRT]\n<TRNAMT>-55.90\n<FITID>66c1e2a0-0001\n"
        + "<NAME>Pagamento efetuado\n<MEMO>NETFLIX.COM\n</STMTTRN>\n"
        + "<STMTTRN>\n<TRNTYPE>CREDIT\n<DTPOSTED>20260905000000[-3:BRT]\n<TRNAMT>3000.00\n<FITID>66c1e2a0-0002\n"
        + "<MEMO>Transferência recebida pelo Pix - EMPRESA LTDA\n</STMTTRN>\n"
        + "<STMTTRN>\n<TRNTYPE>DEBIT\n<DTPOSTED>20260910000000[-3:BRT]\n<TRNAMT>-1234.56\n<FITID>66c1e2a0-0003\n"
        + "<NAME>Compra no débito\n</STMTTRN>\n"
        + "</BANKTRANLIST>\n<LEDGERBAL>\n<BALAMT>1709.54\n<DTASOF>20260917000000[-3:BRT]\n</LEDGERBAL>\n"
        + "</STMTRS>\n</STMTTRNRS>\n</BANKMSGSRSV1>\n</OFX>\n";

    private static OfxStatement Statement(string text)
    {
        var result = OfxParser.Parse(text);

        Assert.Null(result.Error);

        return result.Statement!;
    }

    /// <summary>Spec unit test 18.</summary>
    [Fact]
    public void A_minimal_1x_document_with_unclosed_tags_parses()
    {
        var statement = Statement(Minimal);

        Assert.Equal(3, statement.Rows.Count);
        Assert.All(statement.Rows, row => Assert.Empty(row.Issues));

        Assert.Equal(new DateOnly(2026, 9, 2), statement.Rows[0].Date);
        Assert.Equal(-55.90m, statement.Rows[0].Amount);
        Assert.Equal(3000.00m, statement.Rows[1].Amount);
        Assert.Equal(-1234.56m, statement.Rows[2].Amount);
        Assert.Equal([1, 2, 3], statement.Rows.Select(row => row.RowNumber));
    }

    /// <summary>Spec unit test 19.</summary>
    [Fact]
    public void Name_and_memo_are_joined_with_an_em_dash() =>
        Assert.Equal("Pagamento efetuado — NETFLIX.COM", Statement(Minimal).Rows[0].RawDescription);

    /// <summary>Spec unit test 20, and the mirror: Nubank writes MEMO only.</summary>
    [Fact]
    public void A_missing_side_leaves_the_other_alone()
    {
        var statement = Statement(Minimal);

        Assert.Equal("Transferência recebida pelo Pix - EMPRESA LTDA", statement.Rows[1].RawDescription);
        Assert.Equal("Compra no débito", statement.Rows[2].RawDescription);
    }

    [Fact]
    public void A_memo_that_repeats_the_name_is_kept_once()
    {
        var statement = Statement(Block("<DTPOSTED>20260902\n<TRNAMT>-1\n<NAME>UBER TRIP\n<MEMO>UBER TRIP\n"));

        Assert.Equal("UBER TRIP", statement.Rows[0].RawDescription);
    }

    /// <summary>Spec unit test 21.</summary>
    [Fact]
    public void Fitid_is_extracted() =>
        Assert.Equal(["66c1e2a0-0001", "66c1e2a0-0002", "66c1e2a0-0003"], Statement(Minimal).Rows.Select(row => row.ExternalId));

    /// <summary>Spec unit test 22, and every row inherits it.</summary>
    [Fact]
    public void Curdef_is_extracted()
    {
        var statement = Statement(Minimal);

        Assert.Equal("BRL", statement.Currency);
        Assert.All(statement.Rows, row => Assert.Equal("BRL", row.Currency));
    }

    /// <summary>
    /// Spec decision 9: a foreign-currency line is invalid per row, so the row has
    /// to carry the currency the bank put on it.
    /// </summary>
    [Fact]
    public void A_per_transaction_currency_overrides_the_statement_currency()
    {
        var statement = Statement(
            Block("<DTPOSTED>20260902\n<TRNAMT>-10\n<NAME>A\n")
            + Block("<DTPOSTED>20260902\n<TRNAMT>-10\n<NAME>B\n<CURRENCY>\n<CURRATE>5.1\n<CURSYM>usd\n</CURRENCY>\n"));

        Assert.Equal("BRL", statement.Rows[0].Currency);
        Assert.Equal("USD", statement.Rows[1].Currency);
    }

    /// <summary>Spec unit test 23: a message, never an exception, whatever comes in.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   \n")]
    [InlineData("Data;Valor\n10/09/2026;-1,00\n")]
    [InlineData("<html><body>Sessão expirada</body></html>")]
    [InlineData("OFXHEADER:100\nDATA:OFXSGML\n\n<STMTTRN><DTPOSTED>20260902")]
    [InlineData("\0 binary garbage ÿ")]
    public void Something_that_is_not_ofx_fails_with_a_message(string text)
    {
        var result = OfxParser.Parse(text);

        Assert.Null(result.Statement);
        Assert.Equal(OfxParser.NotOfx, result.Error);
    }

    /// <summary>
    /// Broken rows inside a real document are issues on those rows. The file is not
    /// rejected for them, and the good rows still come through.
    /// </summary>
    [Fact]
    public void Broken_transactions_become_rows_with_issues_and_the_rest_parse()
    {
        var statement = Statement(
            Block("<TRNAMT>-1\n<NAME>Sem data\n")
            + Block("<DTPOSTED>2026-09-02\n<TRNAMT>-1\n<NAME>Data com hífens\n")
            + Block("<DTPOSTED>20261302\n<TRNAMT>-1\n<NAME>Mês treze\n")
            + Block("<DTPOSTED>20260902\n<TRNAMT>abc\n<NAME>Valor em texto\n")
            + Block("<DTPOSTED>20260902\n<NAME>Sem valor\n")
            + Block("<DTPOSTED>20260902\n<TRNAMT>-1\n<NAME>Ok\n"));

        Assert.Equal(6, statement.Rows.Count);
        Assert.Equal([RowIssues.InvalidDate("")], statement.Rows[0].Issues);
        Assert.Equal([RowIssues.InvalidDate("2026-09-02")], statement.Rows[1].Issues);
        Assert.Equal([RowIssues.InvalidDate("20261302")], statement.Rows[2].Issues);
        Assert.Equal([RowIssues.InvalidAmount("abc")], statement.Rows[3].Issues);
        Assert.Equal([RowIssues.InvalidAmount("")], statement.Rows[4].Issues);
        Assert.Empty(statement.Rows[5].Issues);
        Assert.Equal(6, statement.Rows[5].RowNumber);
    }

    [Fact]
    public void A_file_truncated_mid_transaction_keeps_what_it_has()
    {
        var truncated = Minimal[..Minimal.IndexOf("<FITID>66c1e2a0-0003", StringComparison.Ordinal)];

        var statement = Statement(truncated);

        Assert.Equal(3, statement.Rows.Count);
        Assert.Equal(-1234.56m, statement.Rows[2].Amount);
        Assert.Null(statement.Rows[2].ExternalId);
    }

    [Fact]
    public void A_document_with_no_transactions_is_an_empty_statement_not_an_error()
    {
        var statement = Statement("<OFX><STMTRS><CURDEF>BRL<BANKTRANLIST><DTSTART>20260901<DTEND>20260930</BANKTRANLIST></STMTRS></OFX>");

        Assert.Empty(statement.Rows);
        Assert.Equal("BRL", statement.Currency);
    }

    /// <summary>Spec unit test 24: the bank's local day, whatever follows it.</summary>
    [Theory]
    [InlineData("20260902000000[-3:BRT]")]
    [InlineData("20260902000000[-03:EST]")]
    [InlineData("20260902000000[-3:GMT]")]
    [InlineData("20260902120000.000[-3:BRT]")]
    [InlineData("20260902235959")]
    [InlineData("20260902")]
    [InlineData("  20260902  ")]
    public void Dtposted_with_any_suffix_yields_the_calendar_day_written(string posted)
    {
        var statement = Statement(Block($"<DTPOSTED>{posted}\n<TRNAMT>-1\n<NAME>A\n"));

        Assert.Equal(new DateOnly(2026, 9, 2), statement.Rows[0].Date);
        Assert.Empty(statement.Rows[0].Issues);
    }

    /// <summary>
    /// The specification says decimal point. Some Brazilian banks write a decimal
    /// comma anyway, and a thousands separator with it.
    /// </summary>
    [Theory]
    [InlineData("-55.90", "-55.90")]
    [InlineData("+4000.00", "4000.00")]
    [InlineData("3000", "3000")]
    [InlineData("-55,90", "-55.90")]
    [InlineData("-1.234,56", "-1234.56")]
    [InlineData("-1,234.56", "-1234.56")]
    [InlineData("-0.01", "-0.01")]
    public void Trnamt_is_read_as_the_string_says(string written, string expected)
    {
        var statement = Statement(Block($"<DTPOSTED>20260902\n<TRNAMT>{written}\n<NAME>A\n"));

        Assert.Equal(decimal.Parse(expected, System.Globalization.CultureInfo.InvariantCulture), statement.Rows[0].Amount);
    }

    [Fact]
    public void Ofx_2x_xml_with_closed_tags_parses_the_same()
    {
        const string xml =
            "<?xml version=\"1.0\" encoding=\"UTF-8\"?>\n<?OFX OFXHEADER=\"200\" VERSION=\"211\"?>\n"
            + "<OFX><BANKMSGSRSV1><STMTTRNRS><STMTRS><CURDEF>BRL</CURDEF><BANKTRANLIST>"
            + "<STMTTRN><TRNTYPE>DEBIT</TRNTYPE><DTPOSTED>20260902120000[-3:BRT]</DTPOSTED><TRNAMT>-55.90</TRNAMT>"
            + "<FITID>ABC</FITID><NAME>Loja &amp; Cia</NAME><MEMO>Compra &lt;online&gt;</MEMO></STMTTRN>"
            + "</BANKTRANLIST></STMTRS></STMTTRNRS></BANKMSGSRSV1></OFX>";

        var statement = Statement(xml);

        var row = Assert.Single(statement.Rows);

        Assert.Equal(new DateOnly(2026, 9, 2), row.Date);
        Assert.Equal(-55.90m, row.Amount);
        Assert.Equal("ABC", row.ExternalId);
        Assert.Equal("Loja & Cia — Compra <online>", row.RawDescription);
    }

    [Fact]
    public void Tags_match_regardless_of_case()
    {
        var statement = Statement("<ofx><curdef>brl<stmttrn><dtposted>20260902<trnamt>-1<fitid>x<name>A</stmttrn></ofx>");

        var row = Assert.Single(statement.Rows);

        Assert.Equal("BRL", row.Currency);
        Assert.Equal("x", row.ExternalId);
    }

    [Fact]
    public void Blocks_without_a_closing_tag_are_split_at_the_next_block()
    {
        var statement = Statement(
            "<OFX><CURDEF>BRL<BANKTRANLIST>"
            + "<STMTTRN><DTPOSTED>20260902<TRNAMT>-1<FITID>A<NAME>Primeiro"
            + "<STMTTRN><DTPOSTED>20260903<TRNAMT>-2<FITID>B<NAME>Segundo"
            + "</BANKTRANLIST></OFX>");

        Assert.Equal(2, statement.Rows.Count);
        Assert.Equal("A", statement.Rows[0].ExternalId);
        Assert.Equal("Primeiro", statement.Rows[0].RawDescription);
        Assert.Equal("B", statement.Rows[1].ExternalId);
    }

    /// <summary>
    /// Banco do Brasil repeats a FITID across distinct rows. The parser hands both
    /// over as written; deciding that the second is a duplicate is the matcher's
    /// job, and the user can include it from the preview.
    /// </summary>
    [Fact]
    public void A_repeated_fitid_is_passed_through_untouched()
    {
        var statement = Statement(
            Block("<DTPOSTED>20260902\n<TRNAMT>-1\n<FITID>SAME\n<NAME>A\n")
            + Block("<DTPOSTED>20260903\n<TRNAMT>-2\n<FITID>SAME\n<NAME>B\n"));

        Assert.Equal(["SAME", "SAME"], statement.Rows.Select(row => row.ExternalId));
    }

    [Fact]
    public void Windows_1252_bytes_decode_before_parsing()
    {
        var bytes = Encoding.Latin1.GetBytes(Minimal);

        var statement = Statement(StatementText.Decode(bytes));

        Assert.Equal("Transferência recebida pelo Pix - EMPRESA LTDA", statement.Rows[1].RawDescription);
    }

    /// <summary>The upload limit is 5 000 rows; the scanner must stay linear.</summary>
    [Fact]
    public void Five_thousand_transactions_parse_quickly()
    {
        var builder = new StringBuilder("<OFX><CURDEF>BRL<BANKTRANLIST>");

        for (var index = 0; index < 5_000; index++)
        {
            builder.Append("<STMTTRN><TRNTYPE>DEBIT<DTPOSTED>20260902000000[-3:BRT]<TRNAMT>-42.90<FITID>F")
                .Append(index)
                .Append("<NAME>PAG*IFOOD<MEMO>Compra ")
                .Append(index)
                .Append("</STMTTRN>\n");
        }

        builder.Append("</BANKTRANLIST></OFX>");

        var started = System.Diagnostics.Stopwatch.StartNew();
        var statement = Statement(builder.ToString());

        Assert.Equal(5_000, statement.Rows.Count);
        Assert.True(started.ElapsedMilliseconds < 2_000, $"took {started.ElapsedMilliseconds} ms");
    }

    private static string Block(string body) => "<OFX><CURDEF>BRL<STMTTRN>\n" + body + "</STMTTRN></OFX>";
}
