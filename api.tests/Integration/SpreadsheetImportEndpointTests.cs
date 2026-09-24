using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// Spec 011 integration tests 11–15: a Banco do Brasil-like <c>.xlsx</c> (two lines
/// above the table, typed dates and amounts, two balance lines without a value) through
/// the preview, the upload, the commit and dedupe against the same statement as CSV.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class SpreadsheetImportEndpointTests(PostgresFixture postgres)
{
    private const string NotASpreadsheet =
        "O arquivo não é uma planilha do Excel (.xls ou .xlsx) válida. Se o banco oferece CSV ou OFX, exporte nesse formato.";

    /// <summary>The eight valid rows of <c>bb-extrato.xlsx</c>, as the same bank would write them in a CSV.</summary>
    private const string SameStatementAsCsv =
        "Data;Lançamento;Detalhes;Valor (R$)\n"
        + "01/08/2026;Pix - Recebido;ACME TECNOLOGIA LTDA;4.500,00\n"
        + "03/08/2026;Compra com Cartão;SUPERMERCADO ZONA SUL;-187,43\n"
        + "05/08/2026;Pagamento de Boleto;CONDOMINIO EDIFICIO PRIMAVERA;-680,00\n"
        + "08/08/2026;Pix - Enviado;JOÃO DA SILVA;-250,00\n"
        + "12/08/2026;Tarifa Pacote de Serviços;;-39,90\n"
        + "15/08/2026;Compra com Cartão;POSTO IPIRANGA;-212,35\n"
        + "20/08/2026;Rende Fácil;;0,30\n"
        + "25/08/2026;Pix - Recebido;MARIA SOUZA;1.234,56\n";

    private sealed record Preview(List<string> Headers, List<List<string>> SampleRows, string? Delimiter, int SkippedRows, int RowCount);

    /// <summary>Spec test 11.</summary>
    [Fact]
    public async Task An_xlsx_with_an_inline_mapping_is_staged_and_committed_with_exact_amounts()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("xlsx-inline", cancellationToken);
        var accountId = await user.CreateAccountAsync("Banco do Brasil", cancellationToken);

        var staged = await UploadAsync(user, accountId, Fixture(), cancellationToken);
        Assert.Equal((10, 8, 2), (staged.RowCount, staged.Ready, staged.Invalid));

        Assert.Equal(8, (await user.CommitAsync(staged.BatchId, cancellationToken)).Committed);

        var amounts = (await user.ListTransactionsAsync(cancellationToken)).Items.Select(item => item.Amount).ToList();
        Assert.Contains(0.30m, amounts);
        Assert.Contains(-187.43m, amounts);
        Assert.Equal(4365.18m, amounts.Sum());
    }

    /// <summary>Spec test 12.</summary>
    [Fact]
    public async Task Preview_renders_typed_cells_in_the_requested_culture_and_date_format()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("xlsx-preview", cancellationToken);

        var brazilian = await PreviewAsync(user, new Dictionary<string, string>(), cancellationToken);
        Assert.Null(brazilian.Delimiter);
        Assert.Equal(2, brazilian.SkippedRows);
        Assert.Equal(["Data", "Lançamento", "Detalhes", "Valor (R$)"], brazilian.Headers);
        Assert.Equal(["03/08/2026", "Compra com Cartão", "SUPERMERCADO ZONA SUL", "-187,43"], brazilian.SampleRows[2]);

        var american = await PreviewAsync(
            user,
            new Dictionary<string, string> { ["culture"] = "en-US", ["dateFormat"] = "MM/dd/yyyy" },
            cancellationToken);
        Assert.Equal(["08/03/2026", "Compra com Cartão", "SUPERMERCADO ZONA SUL", "-187.43"], american.SampleRows[2]);

        Assert.Empty((await user.Client.GetFromJsonAsync<List<ImportFixtures.BatchItem>>("/api/imports", cancellationToken))!);
    }

    /// <summary>Spec test 13, at the upload and at the preview.</summary>
    [Fact]
    public async Task A_file_that_is_not_a_spreadsheet_is_a_400_with_the_message()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("xlsx-not", cancellationToken);
        var accountId = await user.CreateAccountAsync("Banco do Brasil", cancellationToken);

        using var upload = await user.Client.SendAsync(
            ImportFixtures.Upload("/api/imports", Encoding.UTF8.GetBytes(SameStatementAsCsv), "extrato.xlsx", Fields(accountId)),
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
        Assert.Equal(NotASpreadsheet, await FileProblemAsync(upload, cancellationToken));

        using var preview = await user.Client.SendAsync(
            ImportFixtures.Upload("/api/imports/preview-csv", Encoding.UTF8.GetBytes("<html><table></table></html>"), "extrato.xls"),
            cancellationToken);
        Assert.Equal(HttpStatusCode.BadRequest, preview.StatusCode);
        Assert.Equal(NotASpreadsheet, await FileProblemAsync(preview, cancellationToken));
    }

    /// <summary>Spec test 14: dedupe does not care which format a row came from.</summary>
    [Fact]
    public async Task The_same_statement_as_csv_then_as_xlsx_is_all_duplicates()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("xlsx-dedupe", cancellationToken);
        var accountId = await user.CreateAccountAsync("Banco do Brasil", cancellationToken);

        using var csv = await user.Client.SendAsync(
            ImportFixtures.Upload(
                "/api/imports",
                Encoding.UTF8.GetBytes(SameStatementAsCsv),
                "extrato.csv",
                ImportFixtures.CsvFields(accountId, "pt-BR", "dd/MM/yyyy", "Signed", "Data", "Valor (R$)", "Lançamento,Detalhes", delimiter: ";")),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, csv.StatusCode);
        var csvBatch = (await csv.Content.ReadFromJsonAsync<ImportFixtures.UploadResult>(cancellationToken))!;
        Assert.Equal(8, (await user.CommitAsync(csvBatch.BatchId, cancellationToken)).Committed);

        var staged = await UploadAsync(user, accountId, Fixture(), cancellationToken);

        Assert.Equal((0, 8, 2), (staged.Ready, staged.Duplicates, staged.Invalid));
    }

    /// <summary>Spec test 15: the spreadsheet batch is as private as any other.</summary>
    [Fact]
    public async Task Another_user_cannot_see_or_commit_a_spreadsheet_batch()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var owner = await factory.SignInNewUserAsync("xlsx-owner", cancellationToken);
        var other = await factory.SignInNewUserAsync("xlsx-other", cancellationToken);
        var accountId = await owner.CreateAccountAsync("Banco do Brasil", cancellationToken);

        var staged = await UploadAsync(owner, accountId, Fixture(), cancellationToken);

        using var read = await other.Client.GetAsync($"/api/imports/{staged.BatchId}", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, read.StatusCode);

        using var commit = await other.Client.SendAsync(ImportFixtures.Post($"/api/imports/{staged.BatchId}/commit"), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, commit.StatusCode);
        Assert.Empty((await owner.ListTransactionsAsync(cancellationToken)).Items);
    }

    private static byte[] Fixture() =>
        File.ReadAllBytes(Path.Combine(AppContext.BaseDirectory, "Fixtures", "Spreadsheets", "bb-extrato.xlsx"));

    private static Dictionary<string, string> Fields(Guid accountId)
    {
        var fields = new Dictionary<string, string>(
            ImportFixtures.CsvFields(accountId, "pt-BR", "dd/MM/yyyy", "Signed", "Data", "Valor (R$)", "Lançamento,Detalhes"))
        {
            ["source"] = "Spreadsheet",
        };

        return fields;
    }

    private static async Task<ImportFixtures.UploadResult> UploadAsync(
        SignedInUser user,
        Guid accountId,
        byte[] file,
        CancellationToken cancellationToken)
    {
        using var response = await user.Client.SendAsync(
            ImportFixtures.Upload("/api/imports", file, "bb-extrato.xlsx", Fields(accountId)),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<ImportFixtures.UploadResult>(cancellationToken))!;
    }

    private static async Task<Preview> PreviewAsync(
        SignedInUser user,
        IReadOnlyDictionary<string, string> fields,
        CancellationToken cancellationToken)
    {
        using var response = await user.Client.SendAsync(
            ImportFixtures.Upload("/api/imports/preview-csv", Fixture(), "bb-extrato.xlsx", fields),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return (await response.Content.ReadFromJsonAsync<Preview>(cancellationToken))!;
    }

    private static async Task<string> FileProblemAsync(HttpResponseMessage response, CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

        return document.RootElement.GetProperty("errors").GetProperty("file")[0].GetString()!;
    }
}
