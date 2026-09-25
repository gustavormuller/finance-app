using System.Net;
using System.Net.Http.Json;
using System.Text;
using Finance.Api.Domain.Import;
using Finance.Api.Domain.Transactions;

namespace Finance.Api.Tests.Integration;

/// <summary>Spec 021 integration tests 7 to 14: <c>GET /api/transactions/export</c>.</summary>
[Collection(nameof(PostgresCollection))]
public sealed class TransactionExportTests(PostgresFixture postgres)
{
    private const string Header = "Data;Descrição;Valor;Moeda;Conta;Categoria;Subcategoria";

    /// <summary>Spec 021 integration test 7.</summary>
    [Fact]
    public async Task The_export_is_a_utf8_csv_with_a_bom_named_after_its_range()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("export-shape", cancellationToken);
        var accountId = await user.CreateAccountAsync("Nubank", cancellationToken);
        var categoryId = await user.CreateCategoryAsync("Groceries", cancellationToken);

        await user.CreateTransactionAsync(TransactionsFixtures.TransactionBody(accountId, categoryId), cancellationToken);

        using var response = await user.Client.GetAsync("/api/transactions/export?from=2026-09-01&to=2026-09-30", cancellationToken);

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        Assert.Equal("text/csv", response.Content.Headers.ContentType!.MediaType);
        Assert.Equal("utf-8", response.Content.Headers.ContentType.CharSet);
        Assert.Equal("attachment", response.Content.Headers.ContentDisposition!.DispositionType);
        Assert.Equal("lancamentos-2026-09-01-a-2026-09-30.csv", response.Content.Headers.ContentDisposition.FileName);

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);

        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);
        Assert.Equal(
            $"{Header}\r\n13/09/2026;Supermarket;-42,90;BRL;Nubank;Groceries;\r\n",
            Encoding.UTF8.GetString(bytes[3..]));
    }

    /// <summary>Spec 021 integration test 8: past the list's largest page, in its order.</summary>
    [Fact]
    public async Task Every_matching_row_is_exported_in_the_lists_order()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("export-all", cancellationToken);
        var accountId = await user.CreateAccountAsync("Nubank", cancellationToken);
        var categoryId = await user.CreateCategoryAsync("Groceries", cancellationToken);

        // Three rows a day, so the order also depends on the creation tiebreak.
        for (var index = 0; index < 60; index++)
        {
            var day = new DateOnly(2026, 9, 1).AddDays(index / 3).ToString("yyyy-MM-dd");
            await user.CreateTransactionAsync(
                TransactionsFixtures.TransactionBody(accountId, categoryId, date: day, description: $"row {index}"),
                cancellationToken);
        }

        await user.CreateTransactionAsync(
            TransactionsFixtures.TransactionBody(accountId, categoryId, date: "2026-08-31", description: "outside"),
            cancellationToken);

        const string range = "from=2026-09-01&to=2026-09-30";
        var records = await ExportAsync(user, range, cancellationToken);

        Assert.Equal(60, records.Count);
        Assert.Equal(await ListedDescriptionsAsync(user, range, cancellationToken), records.Select(record => record[1]));
    }

    /// <summary>Spec 021 integration test 9.</summary>
    [Fact]
    public async Task Each_filter_narrows_the_export_as_it_narrows_the_list()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("export-narrow", cancellationToken);
        var nubank = await user.CreateAccountAsync("Nubank", cancellationToken);
        var inter = await user.CreateAccountAsync("Inter", cancellationToken);
        var groceries = await user.CreateCategoryAsync("Groceries", cancellationToken);
        var travel = await user.CreateCategoryAsync("Travel", cancellationToken);

        await user.CreateTransactionAsync(
            TransactionsFixtures.TransactionBody(nubank, groceries, description: "nubank groceries"), cancellationToken);
        await user.CreateTransactionAsync(
            TransactionsFixtures.TransactionBody(inter, travel, description: "inter travel"), cancellationToken);

        var batch = await user.UploadOfxAsync(inter, ImportFixtures.Ofx(ImportFixtures.OfxRows(2)), cancellationToken);
        await user.CommitAsync(batch.BatchId, cancellationToken);

        foreach (var (query, expected) in new[]
        {
            ($"accountId={nubank}", 1),
            ($"accountId={inter}", 3),
            ($"categoryId={travel}", 1),
            ($"importBatchId={batch.BatchId}", 2),
        })
        {
            var records = await ExportAsync(user, query, cancellationToken);

            Assert.Equal(expected, records.Count);
            Assert.Equal(await ListedDescriptionsAsync(user, query, cancellationToken), records.Select(record => record[1]));
        }
    }

    /// <summary>Spec 021 integration tests 10 and 11.</summary>
    [Fact]
    public async Task A_record_carries_the_main_category_the_subcategory_and_the_exact_amount()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("export-record", cancellationToken);
        var accountId = await user.CreateAccountAsync("Nubank", cancellationToken);
        var seeded = await user.CategoriesAsync(cancellationToken);
        var supermarket = await user.CreateCategoryAsync(
            "Supermercado", "Expense", seeded["Alimentação"].Id, cancellationToken);

        await user.CreateTransactionAsync(TransactionsFixtures.TransactionBody(
            accountId, supermarket, -1234.56m, date: "2026-09-03", description: "child"), cancellationToken);
        await user.CreateTransactionAsync(TransactionsFixtures.TransactionBody(
            accountId, seeded["Outros"].Id, -0.01m, date: "2026-09-02", description: "main"), cancellationToken);
        await user.CreateTransactionAsync(TransactionsFixtures.TransactionBody(
            accountId, seeded["Salário"].Id, 1234567890.12m, date: "2026-09-01", description: "income"), cancellationToken);

        var records = await ExportAsync(user, "", cancellationToken);

        Assert.Equal(["03/09/2026", "child", "-1234,56", "BRL", "Nubank", "Alimentação", "Supermercado"], records[0]);
        Assert.Equal(["02/09/2026", "main", "-0,01", "BRL", "Nubank", "Outros", ""], records[1]);
        Assert.Equal(["01/09/2026", "income", "1234567890,12", "BRL", "Nubank", "Salário", ""], records[2]);
    }

    /// <summary>Spec 021 integration test 12.</summary>
    [Fact]
    public async Task Descriptions_are_quoted_and_a_formula_is_guarded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("export-quoting", cancellationToken);
        var accountId = await user.CreateAccountAsync("Nubank", cancellationToken);
        var categoryId = await user.CreateCategoryAsync("Groceries", cancellationToken);

        await user.CreateTransactionAsync(TransactionsFixtures.TransactionBody(
            accountId, categoryId, date: "2026-09-02", description: "Mercado; \"Pão\"\nsegunda linha"), cancellationToken);
        await user.CreateTransactionAsync(TransactionsFixtures.TransactionBody(
            accountId, categoryId, date: "2026-09-01", description: "=1+1"), cancellationToken);

        var text = await ExportTextAsync(user, "", cancellationToken);

        Assert.Contains("02/09/2026;\"Mercado; \"\"Pão\"\"\nsegunda linha\";-42,90;", text, StringComparison.Ordinal);
        Assert.Contains("01/09/2026;'=1+1;-42,90;", text, StringComparison.Ordinal);
    }

    /// <summary>Spec 021 integration test 13: the export refuses what the list refuses, in the same words.</summary>
    [Theory]
    [InlineData("from=2026-13-01", "from")]
    [InlineData("to=31/12/2026", "to")]
    [InlineData("accountId=nubank", "accountId")]
    [InlineData("categoryId=42", "categoryId")]
    [InlineData("importBatchId=ontem", "importBatchId")]
    public async Task A_malformed_filter_is_refused_as_the_list_refuses_it(string query, string field)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("export-bad-filter", cancellationToken);

        using var export = await user.Client.GetAsync($"/api/transactions/export?{query}", cancellationToken);
        using var list = await user.Client.GetAsync($"/api/transactions?{query}", cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, export.StatusCode);
        Assert.Equal(HttpStatusCode.BadRequest, list.StatusCode);

        var messages = await TransactionsFixtures.ProblemMessagesAsync(export, field, cancellationToken);
        Assert.Single(messages);
        Assert.Equal(messages, await TransactionsFixtures.ProblemMessagesAsync(list, field, cancellationToken));
    }

    /// <summary>Spec 021 integration test 14.</summary>
    [Fact]
    public async Task Another_users_rows_are_never_exported()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var userA = await factory.SignInNewUserAsync("export-iso-a", cancellationToken);
        var userB = await factory.SignInNewUserAsync("export-iso-b", cancellationToken);
        var accountId = await userA.CreateAccountAsync("A's bank", cancellationToken);
        var categoryId = await userA.CreateCategoryAsync("A's category", cancellationToken);

        await userA.CreateTransactionAsync(TransactionsFixtures.TransactionBody(accountId, categoryId), cancellationToken);

        Assert.Empty(await ExportAsync(userB, "", cancellationToken));
        Assert.Empty(await ExportAsync(userB, $"accountId={accountId}", cancellationToken));
        Assert.Single(await ExportAsync(userA, "", cancellationToken));

        using var anonymous = factory.CreateApiClient();
        using var response = await anonymous.GetAsync("/api/transactions/export", cancellationToken);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    /// <summary>The body after the BOM, which every export starts with.</summary>
    private static async Task<string> ExportTextAsync(SignedInUser user, string query, CancellationToken cancellationToken)
    {
        using var response = await user.Client.GetAsync($"/api/transactions/export?{query}", cancellationToken);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var bytes = await response.Content.ReadAsByteArrayAsync(cancellationToken);
        Assert.Equal(new byte[] { 0xEF, 0xBB, 0xBF }, bytes[..3]);

        return Encoding.UTF8.GetString(bytes[3..]);
    }

    /// <summary>The records under the header, read by the import's own CSV parser.</summary>
    private static async Task<List<IReadOnlyList<string>>> ExportAsync(SignedInUser user, string query, CancellationToken cancellationToken)
    {
        var table = CsvStatementParser.Parse(await ExportTextAsync(user, query, cancellationToken), TransactionCsv.Separator, hasHeader: true);

        Assert.Equal(Header.Split(';'), table.Headers);
        Assert.All(table.Records, record => Assert.Null(record.Issue));

        return [.. table.Records.Select(record => record.Fields)];
    }

    private static async Task<List<string>> ListedDescriptionsAsync(SignedInUser user, string query, CancellationToken cancellationToken)
    {
        var page = await user.Client.GetFromJsonAsync<TransactionsFixtures.TransactionPage>(
            $"/api/transactions?{query}&pageSize=200", cancellationToken);

        return [.. page!.Items.Select(item => item.Description)];
    }
}
