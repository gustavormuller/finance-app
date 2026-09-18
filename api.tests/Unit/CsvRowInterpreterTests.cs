using Finance.Api.Domain.Import;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// The mapping applied to real layouts: spec unit tests 6-13 again, this time
/// through a template rather than a bare parser call, on the files people actually
/// export.
/// </summary>
public sealed class CsvRowInterpreterTests
{
    private static CsvMapping Signed(
        string culture,
        string dateFormat,
        string dateColumn,
        string amountColumn,
        string descriptionColumns,
        SignMode mode = SignMode.Signed,
        char delimiter = ';',
        bool hasHeader = true) =>
        new(delimiter, hasHeader, culture, dateFormat, mode, dateColumn, amountColumn, null, null, descriptionColumns);

    private static IReadOnlyList<ParsedRow> Interpret(string text, CsvMapping mapping)
    {
        var table = CsvStatementParser.Parse(text, mapping.Delimiter, mapping.HasHeader);

        Assert.Empty(mapping.Validate(table));

        return CsvRowInterpreter.Interpret(table, mapping);
    }

    /// <summary>Nubank account statement: comma delimiter, dd/MM/yyyy, dot decimals, signed.</summary>
    [Fact]
    public void Nubank_account_export()
    {
        const string nubank =
            "Data,Valor,Identificador,Descrição\n"
            + "24/08/2026,-58.00,66c1e2a0-1111-4b2b-9c3d-000000000001,Compra no débito - Uber\n"
            + "25/08/2026,1500.00,66c1e2a0-1111-4b2b-9c3d-000000000002,Transferência recebida pelo Pix - JOÃO\n";

        var rows = Interpret(nubank, Signed("en-US", "dd/MM/yyyy", "Data", "Valor", "Descrição", delimiter: ','));

        Assert.Equal(2, rows.Count);
        Assert.Equal(new DateOnly(2026, 8, 24), rows[0].Date);
        Assert.Equal(-58.00m, rows[0].Amount);
        Assert.Equal("Compra no débito - Uber", rows[0].RawDescription);
        Assert.Equal(1500.00m, rows[1].Amount);
        Assert.All(rows, row => Assert.Empty(row.Issues));
        Assert.All(rows, row => Assert.Null(row.Currency));
        Assert.All(rows, row => Assert.Null(row.ExternalId));
    }

    /// <summary>Nubank card statement: ISO dates, purchases positive, so SignedInverted.</summary>
    [Fact]
    public void Nubank_card_export_with_inverted_sign()
    {
        const string card =
            "date,category,title,amount\n"
            + "2026-08-24,transporte,Uber *Trip,58.00\n"
            + "2026-08-25,outros,Pagamento recebido,-1200.00\n";

        var rows = Interpret(card, Signed("en-US", "yyyy-MM-dd", "date", "amount", "title", SignMode.SignedInverted, ','));

        Assert.Equal(new DateOnly(2026, 8, 24), rows[0].Date);
        Assert.Equal(-58.00m, rows[0].Amount);
        Assert.Equal(1200.00m, rows[1].Amount);
    }

    /// <summary>Banco Inter: preamble, semicolons, Brazilian decimals, two description columns.</summary>
    [Fact]
    public void Inter_export_joins_two_description_columns()
    {
        const string inter =
            "Extrato Conta Corrente\nConta ;72385499\nPeríodo ;19/08/2026 a 17/09/2026\nSaldo ;1.992,51\n\n"
            + "Data Lançamento;Histórico;Descrição;Valor;Saldo\n"
            + "30/08/2026;Pix enviado;JOAO DA SILVA;-1.250,00;1.742,51\n"
            + "02/09/2026;Pagamento efetuado;;-55,90;1.436,61\n";

        var rows = Interpret(inter, Signed("pt-BR", "dd/MM/yyyy", "Data Lançamento", "Valor", "Histórico, Descrição"));

        Assert.Equal("Pix enviado — JOAO DA SILVA", rows[0].RawDescription);
        Assert.Equal(-1250.00m, rows[0].Amount);

        // An empty part is left out rather than producing "Pagamento efetuado — ".
        Assert.Equal("Pagamento efetuado", rows[1].RawDescription);
    }

    /// <summary>The generic accounting layout with separate debit and credit columns.</summary>
    [Fact]
    public void Debit_and_credit_columns()
    {
        const string sheet =
            "Data;Lançamento;Crédito (R$);Débito (R$);Saldo (R$)\n"
            + "01/09/2026;Salário;3.000,00;;3.000,00\n"
            + "02/09/2026;Conta de luz;;150,00;2.850,00\n"
            + "03/09/2026;Estorno;0,00;R$ 20,00;2.830,00\n"
            + "04/09/2026;Erro;10,00;10,00;2.830,00\n"
            + "05/09/2026;Sem valor;;;2.830,00\n";

        var mapping = new CsvMapping(';', true, "pt-BR", "dd/MM/yyyy", SignMode.DebitCredit,
            "Data", null, "Débito (R$)", "Crédito (R$)", "Lançamento");

        var rows = Interpret(sheet, mapping);

        Assert.Equal(3000.00m, rows[0].Amount);
        Assert.Equal(-150.00m, rows[1].Amount);
        Assert.Equal(-20.00m, rows[2].Amount);

        Assert.Null(rows[3].Amount);
        Assert.Equal([RowIssues.DebitAndCredit], rows[3].Issues);

        Assert.Null(rows[4].Amount);
        Assert.Equal([RowIssues.MissingAmount], rows[4].Issues);
    }

    [Fact]
    public void Columns_may_be_referenced_by_index_when_there_is_no_header()
    {
        const string headerless = "10/09/2026;Loja;-1,00\n11/09/2026;Outra;-2,00\n";

        var rows = Interpret(headerless, Signed("pt-BR", "dd/MM/yyyy", "0", "2", "1", hasHeader: false));

        Assert.Equal(2, rows.Count);
        Assert.Equal("Loja", rows[0].RawDescription);
        Assert.Equal(-1.00m, rows[0].Amount);
    }

    [Fact]
    public void Header_names_match_case_insensitively_and_trimmed()
    {
        var rows = Interpret(
            " Data ;VALOR;Descrição\n10/09/2026;-1,00;Loja\n",
            Signed("pt-BR", "dd/MM/yyyy", "data", "valor", "descrição"));

        Assert.Equal(-1.00m, Assert.Single(rows).Amount);
    }

    /// <summary>
    /// Spec unit tests 11-13 through a template. The same file under the two formats
    /// gives two different days, and the day that cannot be a month is an issue on
    /// that row, not a rollover and not a swap.
    /// </summary>
    [Fact]
    public void The_declared_date_format_decides_and_a_misfit_is_an_issue_on_that_row()
    {
        const string file = "Data;Valor;Descrição\n03/04/2026;-1,00;A\n31/12/2026;-2,00;B\n";

        var dayFirst = Interpret(file, Signed("pt-BR", "dd/MM/yyyy", "Data", "Valor", "Descrição"));
        var monthFirst = Interpret(file, Signed("pt-BR", "MM/dd/yyyy", "Data", "Valor", "Descrição"));

        Assert.Equal(new DateOnly(2026, 4, 3), dayFirst[0].Date);
        Assert.Equal(new DateOnly(2026, 12, 31), dayFirst[1].Date);

        Assert.Equal(new DateOnly(2026, 3, 4), monthFirst[0].Date);
        Assert.Null(monthFirst[1].Date);
        Assert.Equal([RowIssues.InvalidDate("31/12/2026")], monthFirst[1].Issues);

        // The amount on the bad-date row still parsed: one issue per problem.
        Assert.Equal(-2.00m, monthFirst[1].Amount);
    }

    /// <summary>Spec unit test 8 through a template: the wrong culture is an issue, never a wrong number.</summary>
    [Fact]
    public void An_amount_in_the_other_culture_is_an_issue_on_that_row()
    {
        var rows = Interpret(
            "Data;Valor;Descrição\n10/09/2026;1.234,56;A\n11/09/2026;12.50;B\n",
            Signed("en-US", "dd/MM/yyyy", "Data", "Valor", "Descrição"));

        Assert.Null(rows[0].Amount);
        Assert.Equal([RowIssues.InvalidAmount("1.234,56")], rows[0].Issues);
        Assert.Equal(12.50m, rows[1].Amount);
    }

    [Fact]
    public void A_ragged_record_becomes_a_row_with_only_that_issue()
    {
        var rows = Interpret(
            "Data;Valor;Descrição\n10/09/2026;-1,00\n11/09/2026;-2,00;Ok\n",
            Signed("pt-BR", "dd/MM/yyyy", "Data", "Valor", "Descrição"));

        Assert.Equal([RowIssues.RaggedRow(2, 3)], rows[0].Issues);
        Assert.Null(rows[0].Date);
        Assert.Equal(2, rows[0].RowNumber);
        Assert.Empty(rows[1].Issues);
    }

    [Fact]
    public void Every_problem_on_a_row_is_reported_together()
    {
        var rows = Interpret(
            "Data;Valor;Descrição\nontem;muito;A\n",
            Signed("pt-BR", "dd/MM/yyyy", "Data", "Valor", "Descrição"));

        Assert.Equal([RowIssues.InvalidDate("ontem"), RowIssues.InvalidAmount("muito")], rows[0].Issues);
    }

    [Fact]
    public void Validation_names_the_field_for_every_mapping_problem()
    {
        var table = CsvStatementParser.Parse("Data;Valor;Descrição\n10/09/2026;-1,00;A\n", ';', hasHeader: true);

        var mapping = new CsvMapping(';', true, "xx-XX", "dd/MM", SignMode.DebitCredit,
            "Dia", null, null, "Crédito", "Descrição, Nada");

        var fields = mapping.Validate(table).Select(violation => violation.Field).ToList();

        Assert.Equal(["culture", "dateFormat", "dateColumn", "debitColumn", "creditColumn", "descriptionColumns"], fields);
    }

    [Fact]
    public void A_valid_mapping_has_no_violations()
    {
        var table = CsvStatementParser.Parse("Data;Valor;Descrição\n10/09/2026;-1,00;A\n", ';', hasHeader: true);

        Assert.Empty(Signed("pt-BR", "dd/MM/yyyy", "Data", "Valor", "Descrição").Validate(table));
        Assert.Empty(Signed("pt-BR", "dd/MM/yyyy", "0", "1", "2").Validate(table));

        Assert.Single(Signed("pt-BR", "dd/MM/yyyy", "3", "1", "2").Validate(table));
    }
}
