using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using Finance.Api.Domain.Import;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// The CSV path over HTTP: the preview, an upload with an inline mapping, an
/// upload through a saved template, and the two failures that matter most — a
/// column that does not exist and a date format that does not fit.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class CsvImportEndpointTests(PostgresFixture postgres)
{
    private const string Nubank =
        "Data,Valor,Identificador,Descrição\n"
        + "24/08/2026,-58.00,66c1e2a0-0001,Compra no débito - Uber\n"
        + "25/08/2026,1500.00,66c1e2a0-0002,Transferência recebida pelo Pix - JOÃO\n"
        + "03/04/2026,-12.50,66c1e2a0-0003,Padaria\n";

    private const string Inter =
        "Extrato Conta Corrente\nConta ;72385499\nPeríodo ;19/08/2026 a 17/09/2026\nSaldo ;1.992,51\n\n"
        + "Data Lançamento;Histórico;Descrição;Valor;Saldo\n"
        + "30/08/2026;Pix enviado;JOAO DA SILVA;-1.250,00;1.742,51\n"
        + "02/09/2026;Pagamento efetuado;NETFLIX.COM;-55,90;1.436,61\n";

    private sealed record Preview(List<string> Headers, List<List<string>> SampleRows, string Delimiter, int SkippedRows, int RowCount);

    [Fact]
    public async Task Preview_returns_the_real_headers_and_the_first_rows_without_persisting_anything()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("csv-preview", cancellationToken);

        using var response = await user.Client.SendAsync(
            ImportFixtures.Upload("/api/imports/preview-csv", Encoding.Latin1.GetBytes(Inter), "extrato.csv"),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var preview = (await response.Content.ReadFromJsonAsync<Preview>(cancellationToken))!;

        Assert.Equal(";", preview.Delimiter);
        Assert.Equal(4, preview.SkippedRows);
        Assert.Equal(["Data Lançamento", "Histórico", "Descrição", "Valor", "Saldo"], preview.Headers);
        Assert.Equal(2, preview.SampleRows.Count);
        Assert.Equal("-1.250,00", preview.SampleRows[0][3]);

        Assert.Empty((await user.Client.GetFromJsonAsync<List<ImportFixtures.BatchItem>>("/api/imports", cancellationToken))!);

        using var empty = await user.Client.SendAsync(
            ImportFixtures.Upload("/api/imports/preview-csv", Encoding.UTF8.GetBytes("\n\n"), "vazio.csv"),
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, empty.StatusCode);
    }

    [Fact]
    public async Task A_csv_with_an_inline_mapping_is_staged_and_committed_with_exact_amounts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("csv-inline", cancellationToken);
        var accountId = await user.CreateAccountAsync("Nubank", cancellationToken);

        using var response = await user.Client.SendAsync(
            ImportFixtures.Upload(
                "/api/imports",
                Encoding.UTF8.GetBytes(Nubank),
                "nubank.csv",
                ImportFixtures.CsvFields(accountId, "en-US", "dd/MM/yyyy", "Signed", "Data", "Valor", "Descrição", delimiter: ",")),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var staged = (await response.Content.ReadFromJsonAsync<ImportFixtures.UploadResult>(cancellationToken))!;
        Assert.Equal((3, 3, 0), (staged.RowCount, staged.Ready, staged.Invalid));

        var rows = (await user.GetBatchAsync(staged.BatchId, cancellationToken)).Rows.Items;
        Assert.Equal(new DateOnly(2026, 4, 3), rows[2].Date);
        Assert.Equal(-12.50m, rows[2].Amount);
        Assert.All(rows, row => Assert.Null(row.ExternalId));

        Assert.Equal(3, (await user.CommitAsync(staged.BatchId, cancellationToken)).Committed);

        var transactions = (await user.ListTransactionsAsync(cancellationToken)).Items;
        Assert.Contains(transactions, item => item.Description == "Transferência recebida pelo Pix - JOÃO" && item.Amount == 1500.00m);
    }

    [Fact]
    public async Task A_csv_through_a_saved_template_stages_with_the_templates_mapping()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("csv-template", cancellationToken);
        var accountId = await user.CreateAccountAsync("Inter", cancellationToken);

        using var created = await user.Client.SendAsync(
            TransactionsFixtures.Post("/api/csv-templates", new
            {
                name = "Inter",
                delimiter = ";",
                hasHeader = true,
                culture = "pt-BR",
                dateFormat = "dd/MM/yyyy",
                signMode = "Signed",
                dateColumn = "Data Lançamento",
                amountColumn = "Valor",
                descriptionColumns = "Histórico, Descrição",
            }),
            cancellationToken);
        var template = (await created.Content.ReadFromJsonAsync<ImportFixtures.TemplateItem>(cancellationToken))!;

        using var response = await user.Client.SendAsync(
            ImportFixtures.Upload(
                "/api/imports",
                Encoding.Latin1.GetBytes(Inter),
                "inter.csv",
                new Dictionary<string, string>
                {
                    ["accountId"] = accountId.ToString(),
                    ["source"] = "Csv",
                    ["templateId"] = template.Id.ToString(),
                }),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var staged = (await response.Content.ReadFromJsonAsync<ImportFixtures.UploadResult>(cancellationToken))!;
        Assert.Equal(2, staged.Ready);

        var rows = (await user.GetBatchAsync(staged.BatchId, cancellationToken)).Rows.Items;
        Assert.Equal("Pix enviado — JOAO DA SILVA", rows[0].RawDescription);
        Assert.Equal(-1250.00m, rows[0].Amount);
        Assert.Equal(7, rows[0].RowNumber);
    }

    [Fact]
    public async Task A_column_that_is_not_in_the_file_is_a_400_naming_the_field()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("csv-column", cancellationToken);
        var accountId = await user.CreateAccountAsync("Nubank", cancellationToken);

        using var response = await user.Client.SendAsync(
            ImportFixtures.Upload(
                "/api/imports",
                Encoding.UTF8.GetBytes(Nubank),
                "nubank.csv",
                ImportFixtures.CsvFields(accountId, "en-US", "dd/MM/yyyy", "Signed", "Date", "Amount", "Descrição", delimiter: ",")),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal(["amountColumn", "dateColumn"], (await TransactionsFixtures.ProblemFieldsAsync(response, cancellationToken)).Order());
        Assert.Empty((await user.Client.GetFromJsonAsync<List<ImportFixtures.BatchItem>>("/api/imports", cancellationToken))!);
    }

    /// <summary>
    /// An optional column reference is read the way a saved template stores it: trimmed.
    /// The padding never mattered to resolving the column, so the refusal names the
    /// column that was looked for, whether the mapping came inline or from a template.
    /// </summary>
    [Fact]
    public async Task A_padded_optional_column_is_reported_trimmed_as_a_template_would_store_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("csv-padded-column", cancellationToken);
        var accountId = await user.CreateAccountAsync("Nubank", cancellationToken);

        using var response = await user.Client.SendAsync(
            ImportFixtures.Upload(
                "/api/imports",
                Encoding.UTF8.GetBytes(Nubank),
                "nubank.csv",
                ImportFixtures.CsvFields(accountId, "en-US", "dd/MM/yyyy", "Signed", "Data", "  Amount  ", "Descrição", delimiter: ",")),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        Assert.Equal(
            "Coluna \"Amount\" não encontrada no arquivo.",
            problem.RootElement.GetProperty("errors").GetProperty("amountColumn")[0].GetString());
    }

    /// <summary>
    /// The failure mode the spec cares most about, end to end: the wrong format is
    /// loud on the rows it cannot read and never swaps day and month on the ones it
    /// can. 24/08 cannot be a month, 03/04 can — and under MM/dd/yyyy it is March.
    /// </summary>
    [Fact]
    public async Task The_wrong_date_format_fails_loudly_instead_of_swapping_day_and_month()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("csv-format", cancellationToken);
        var accountId = await user.CreateAccountAsync("Nubank", cancellationToken);

        using var response = await user.Client.SendAsync(
            ImportFixtures.Upload(
                "/api/imports",
                Encoding.UTF8.GetBytes(Nubank),
                "nubank.csv",
                ImportFixtures.CsvFields(accountId, "en-US", "MM/dd/yyyy", "Signed", "Data", "Valor", "Descrição", delimiter: ",")),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var staged = (await response.Content.ReadFromJsonAsync<ImportFixtures.UploadResult>(cancellationToken))!;
        Assert.Equal((2, 1), (staged.Invalid, staged.Ready));

        var rows = (await user.GetBatchAsync(staged.BatchId, cancellationToken)).Rows.Items;
        Assert.Equal([RowIssues.InvalidDate("24/08/2026")], rows[0].Issues);
        Assert.Equal([RowIssues.InvalidDate("25/08/2026")], rows[1].Issues);
        Assert.Equal(new DateOnly(2026, 3, 4), rows[2].Date);
    }

    [Fact]
    public async Task A_debit_credit_sheet_in_brazilian_format_resolves_both_sides()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("csv-debit-credit", cancellationToken);
        var accountId = await user.CreateAccountAsync("Itaú", cancellationToken);

        const string sheet =
            "Data;Lançamento;Crédito (R$);Débito (R$);Saldo (R$)\n"
            + "01/09/2026;Salário;3.000,00;;3.000,00\n"
            + "02/09/2026;Conta de luz;;150,00;2.850,00\n"
            + "04/09/2026;Erro;10,00;10,00;2.830,00\n";

        using var response = await user.Client.SendAsync(
            ImportFixtures.Upload(
                "/api/imports",
                Encoding.UTF8.GetBytes(sheet),
                "planilha.csv",
                ImportFixtures.CsvFields(accountId, "pt-BR", "dd/MM/yyyy", "DebitCredit", "Data", null, "Lançamento",
                    debitColumn: "Débito (R$)", creditColumn: "Crédito (R$)")),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var staged = (await response.Content.ReadFromJsonAsync<ImportFixtures.UploadResult>(cancellationToken))!;
        Assert.Equal((2, 1), (staged.Ready, staged.Invalid));

        var rows = (await user.GetBatchAsync(staged.BatchId, cancellationToken)).Rows.Items;
        Assert.Equal(3000.00m, rows[0].Amount);
        Assert.Equal(-150.00m, rows[1].Amount);
        Assert.Equal([RowIssues.DebitAndCredit], rows[2].Issues);
    }
}
