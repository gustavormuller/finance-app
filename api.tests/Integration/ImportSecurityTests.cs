using System.Net;
using System.Net.Http.Json;
using System.Text;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// Spec integration tests 36-39 over HTTP: one user's batches, rows and templates
/// behave, for everybody else, exactly as if they had never been written.
/// </summary>
/// <remarks>
/// Every refusal is a 404 or a 400 and never a 403: a 403 would confirm the id
/// exists. And every test also shows the owner succeeding at the same call, so a
/// route that was simply missing could not pass by answering 404 to everyone.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class ImportSecurityTests(PostgresFixture postgres)
{
    /// <summary>Spec integration tests 36 and 37.</summary>
    [Fact]
    public async Task A_users_batch_is_absent_from_the_other_users_list_and_a_404_by_id()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var userA = await factory.SignInNewUserAsync("import-http-iso-a", cancellationToken);
        var userB = await factory.SignInNewUserAsync("import-http-iso-b", cancellationToken);

        var accountId = await userA.CreateAccountAsync("Nubank", cancellationToken);
        var staged = await userA.UploadOfxAsync(accountId, ImportFixtures.Ofx(ImportFixtures.OfxRows(3)), cancellationToken);

        var mine = await userA.Client.GetFromJsonAsync<List<ImportFixtures.BatchItem>>("/api/imports", cancellationToken);
        var theirs = await userB.Client.GetFromJsonAsync<List<ImportFixtures.BatchItem>>("/api/imports", cancellationToken);

        Assert.Equal(staged.BatchId, Assert.Single(mine!).Id);
        Assert.Empty(theirs!);

        using var byId = await userB.Client.GetAsync($"/api/imports/{staged.BatchId}", cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, byId.StatusCode);

        var detail = await userA.GetBatchAsync(staged.BatchId, cancellationToken);
        Assert.Equal(3, detail.Rows.Total);
    }

    /// <summary>
    /// Spec integration test 38, the one the spec singles out. B committing A's batch
    /// is a 404 that writes nothing: A's batch is still Staged, A has no
    /// transactions, and A can then commit it themselves.
    /// </summary>
    [Fact]
    public async Task Committing_another_users_batch_is_a_404_and_writes_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var userA = await factory.SignInNewUserAsync("import-commit-a", cancellationToken);
        var userB = await factory.SignInNewUserAsync("import-commit-b", cancellationToken);

        var accountId = await userA.CreateAccountAsync("Nubank", cancellationToken);
        var staged = await userA.UploadOfxAsync(accountId, ImportFixtures.Ofx(ImportFixtures.OfxRows(3)), cancellationToken);

        using var commit = await userB.Client.SendAsync(ImportFixtures.Post($"/api/imports/{staged.BatchId}/commit"), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, commit.StatusCode);

        // Nothing written, from A's point of view...
        var detail = await userA.GetBatchAsync(staged.BatchId, cancellationToken);
        Assert.Equal("Staged", detail.Batch.Status);
        Assert.Equal(3, detail.Rows.Total);
        Assert.Equal(0, (await userA.ListTransactionsAsync(cancellationToken)).Total);

        // ...nor from B's...
        Assert.Equal(0, (await userB.ListTransactionsAsync(cancellationToken)).Total);

        // ...nor in the table itself, read past the query filter.
        await using (var unfiltered = TransactionsFixtures.ContextFor(postgres.ConnectionString, userA.Id))
        {
            Assert.Equal(0, await unfiltered.Transactions.IgnoreQueryFilters()
                .CountAsync(transaction => transaction.ImportBatchId == staged.BatchId, cancellationToken));
        }

        // The other mutations answer the same way.
        using var patch = await userB.Client.SendAsync(
            ImportFixtures.Patch($"/api/imports/{staged.BatchId}/rows/{detail.Rows.Items[0].Id}", new { include = false }),
            cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, patch.StatusCode);

        using var discard = await userB.Client.SendAsync(TransactionsFixtures.Delete($"/api/imports/{staged.BatchId}"), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, discard.StatusCode);

        // And the owner's commit works, so a missing route could not have passed this.
        var committed = await userA.CommitAsync(staged.BatchId, cancellationToken);
        Assert.Equal(3, committed.Committed);

        using var undo = await userB.Client.SendAsync(ImportFixtures.Post($"/api/imports/{staged.BatchId}/undo"), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, undo.StatusCode);
        Assert.Equal(3, (await userA.ListTransactionsAsync(cancellationToken)).Total);
    }

    /// <summary>Rule 4 of 003 applies to the upload: another user's account does not resolve.</summary>
    [Fact]
    public async Task Uploading_into_another_users_account_is_a_400_naming_the_field()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var userA = await factory.SignInNewUserAsync("import-account-a", cancellationToken);
        var userB = await factory.SignInNewUserAsync("import-account-b", cancellationToken);

        var accountId = await userA.CreateAccountAsync("Nubank", cancellationToken);

        using var response = await userB.Client.SendAsync(
            ImportFixtures.Upload(
                "/api/imports",
                Encoding.UTF8.GetBytes(ImportFixtures.Ofx(ImportFixtures.OfxRows(1))),
                "extrato.ofx",
                ImportFixtures.OfxFields(accountId)),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("accountId", await TransactionsFixtures.ProblemFieldsAsync(response, cancellationToken));

        Assert.Empty((await userA.Client.GetFromJsonAsync<List<ImportFixtures.BatchItem>>("/api/imports", cancellationToken))!);
    }

    /// <summary>Spec integration test 39.</summary>
    [Fact]
    public async Task Csv_templates_are_isolated()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var userA = await factory.SignInNewUserAsync("template-http-a", cancellationToken);
        var userB = await factory.SignInNewUserAsync("template-http-b", cancellationToken);

        using var created = await userA.Client.SendAsync(
            TransactionsFixtures.Post("/api/csv-templates", CsvTemplateEndpointTests.NubankTemplate("Nubank conta")),
            cancellationToken);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);
        var template = (await created.Content.ReadFromJsonAsync<ImportFixtures.TemplateItem>(cancellationToken))!;

        Assert.Empty((await userB.Client.GetFromJsonAsync<List<ImportFixtures.TemplateItem>>("/api/csv-templates", cancellationToken))!);

        using var delete = await userB.Client.SendAsync(TransactionsFixtures.Delete($"/api/csv-templates/{template.Id}"), cancellationToken);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        // Nor can B import through A's template.
        var accountB = await userB.CreateAccountAsync("Inter", cancellationToken);

        using var upload = await userB.Client.SendAsync(
            ImportFixtures.Upload(
                "/api/imports",
                Encoding.UTF8.GetBytes("Data,Valor,Descrição\n10/09/2026,-1.00,A\n"),
                "extrato.csv",
                new Dictionary<string, string>
                {
                    ["accountId"] = accountB.ToString(),
                    ["source"] = "Csv",
                    ["templateId"] = template.Id.ToString(),
                }),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, upload.StatusCode);
        Assert.Contains("templateId", await TransactionsFixtures.ProblemFieldsAsync(upload, cancellationToken));

        Assert.Single((await userA.Client.GetFromJsonAsync<List<ImportFixtures.TemplateItem>>("/api/csv-templates", cancellationToken))!);
    }

    [Theory]
    [InlineData("/api/imports")]
    [InlineData("/api/csv-templates")]
    public async Task Listing_without_a_session_is_401(string url)
    {
        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
