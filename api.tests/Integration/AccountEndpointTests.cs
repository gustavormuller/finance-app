using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// The accounts resource, and the HTTP half of spec integration test 16.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class AccountEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task An_account_can_be_created_listed_and_edited()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("accounts", cancellationToken);

        using var created = await user.Client.SendAsync(
            TransactionsFixtures.Post("/api/accounts", new
            {
                name = "Nubank",
                type = "Checking",
                currency = "BRL",
            }),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var body = await created.Content.ReadFromJsonAsync<TransactionsFixtures.AccountItem>(cancellationToken);
        Assert.Equal("Nubank", body!.Name);
        Assert.Equal("Checking", body.Type);
        Assert.Equal("BRL", body.Currency);

        // 201 carries a Location, so a client can follow it without guessing the route.
        Assert.Equal($"/api/accounts/{body.Id}", created.Headers.Location?.ToString());

        using var edit = await user.Client.SendAsync(
            TransactionsFixtures.Put($"/api/accounts/{body.Id}", new
            {
                name = "Nubank current",
                type = "Savings",
                currency = "BRL",
            }),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        var accounts = await user.Client.GetFromJsonAsync<List<TransactionsFixtures.AccountItem>>(
            "/api/accounts", cancellationToken);

        var only = Assert.Single(accounts!);
        Assert.Equal("Nubank current", only.Name);
        Assert.Equal("Savings", only.Type);
    }

    /// <summary>
    /// Spec integration test 16 over HTTP: 409 with a reason the UI can show, and
    /// nothing deleted.
    /// </summary>
    [Fact]
    public async Task An_account_with_transactions_cannot_be_deleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("account-409", cancellationToken);
        var accountId = await user.CreateAccountAsync("Nubank", cancellationToken);
        var categoryId = await user.CreateCategoryAsync("Groceries", cancellationToken);

        await user.CreateTransactionAsync(
            TransactionsFixtures.TransactionBody(accountId, categoryId),
            cancellationToken);

        using var response = await user.Client.SendAsync(
            TransactionsFixtures.Delete($"/api/accounts/{accountId}"), cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        // The spec asks the UI to show the reason rather than fail silently, so the
        // body has to carry one — in Portuguese, because it is shown verbatim.
        var reason = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("lançamento", reason, StringComparison.OrdinalIgnoreCase);

        var accounts = await user.Client.GetFromJsonAsync<List<TransactionsFixtures.AccountItem>>(
            "/api/accounts", cancellationToken);

        Assert.Single(accounts!);

        var page = await user.Client.GetFromJsonAsync<TransactionsFixtures.TransactionPage>(
            "/api/transactions", cancellationToken);

        Assert.Equal(1, page!.Total);
    }

    [Fact]
    public async Task An_account_with_no_transactions_is_deleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("account-delete", cancellationToken);
        var accountId = await user.CreateAccountAsync("Disposable", cancellationToken);

        using var response = await user.Client.SendAsync(
            TransactionsFixtures.Delete($"/api/accounts/{accountId}"), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);

        var accounts = await user.Client.GetFromJsonAsync<List<TransactionsFixtures.AccountItem>>(
            "/api/accounts", cancellationToken);

        Assert.Empty(accounts!);
    }

    /// <summary>
    /// 003 amendment 1, test 16a. An import batch holds its account by a RESTRICT key,
    /// so a batch left with no transactions used to pass the check and end in a 500.
    /// </summary>
    [Fact]
    public async Task An_account_with_an_import_in_its_history_cannot_be_deleted_until_it_is_undone()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("account-import", cancellationToken);
        var accountId = await user.CreateAccountAsync("Nubank", cancellationToken);

        var batch = await user.UploadOfxAsync(accountId, ImportFixtures.Ofx(ImportFixtures.OfxRows(2)), cancellationToken);
        await user.CommitAsync(batch.BatchId, cancellationToken);

        // Both in the way: one sentence names both, undo first.
        using var both = await user.Client.SendAsync(
            TransactionsFixtures.Delete($"/api/accounts/{accountId}"), cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, both.StatusCode);
        Assert.Equal(
            "'Nubank' ainda tem 2 lançamento(s) e 1 importação(ões) no histórico. "
            + "Desfaça ou descarte as importações e mova ou exclua os lançamentos restantes antes de excluir a conta.",
            await InvestmentAssetEndpointTests.DetailAsync(both, cancellationToken));

        foreach (var imported in (await user.ListTransactionsAsync(cancellationToken)).Items)
        {
            using var byHand = await user.Client.SendAsync(
                TransactionsFixtures.Delete($"/api/transactions/{imported.Id}"), cancellationToken);

            Assert.Equal(HttpStatusCode.NoContent, byHand.StatusCode);
        }

        // No transactions left, only the batch: this was the 500.
        using var batchOnly = await user.Client.SendAsync(
            TransactionsFixtures.Delete($"/api/accounts/{accountId}"), cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, batchOnly.StatusCode);
        Assert.Equal(
            "'Nubank' ainda tem 1 importação(ões) no histórico. "
            + "Desfaça ou descarte as importações antes de excluir a conta.",
            await InvestmentAssetEndpointTests.DetailAsync(batchOnly, cancellationToken));

        Assert.Single((await user.Client.GetFromJsonAsync<List<TransactionsFixtures.AccountItem>>(
            "/api/accounts", cancellationToken))!);
        Assert.Single((await user.Client.GetFromJsonAsync<List<ImportFixtures.BatchItem>>(
            "/api/imports", cancellationToken))!);

        // The way out the sentence points at.
        using var undo = await user.Client.SendAsync(
            ImportFixtures.Post($"/api/imports/{batch.BatchId}/undo"), cancellationToken);

        Assert.Equal(HttpStatusCode.OK, undo.StatusCode);

        using var deleted = await user.Client.SendAsync(
            TransactionsFixtures.Delete($"/api/accounts/{accountId}"), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    /// <summary>003 amendment 1, test 16b: a batch in review holds its account too.</summary>
    [Fact]
    public async Task An_account_with_an_import_in_review_cannot_be_deleted_until_it_is_discarded()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("account-staged", cancellationToken);
        var accountId = await user.CreateAccountAsync("Inter", cancellationToken);

        var batch = await user.UploadOfxAsync(accountId, ImportFixtures.Ofx(ImportFixtures.OfxRows(2)), cancellationToken);

        using var refused = await user.Client.SendAsync(
            TransactionsFixtures.Delete($"/api/accounts/{accountId}"), cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, refused.StatusCode);
        Assert.Equal(
            "'Inter' ainda tem 1 importação(ões) no histórico. "
            + "Desfaça ou descarte as importações antes de excluir a conta.",
            await InvestmentAssetEndpointTests.DetailAsync(refused, cancellationToken));

        using var discard = await user.Client.SendAsync(
            TransactionsFixtures.Delete($"/api/imports/{batch.BatchId}"), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, discard.StatusCode);

        using var deleted = await user.Client.SendAsync(
            TransactionsFixtures.Delete($"/api/accounts/{accountId}"), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
    }

    [Fact]
    public async Task Two_accounts_cannot_share_a_name_under_one_user()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("account-dupe", cancellationToken);

        await user.CreateAccountAsync("Nubank", cancellationToken);

        using var response = await user.Client.SendAsync(
            TransactionsFixtures.Post("/api/accounts", new
            {
                name = "Nubank",
                type = "Checking",
                currency = "BRL",
            }),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>A currency the Money constructor would reject never reaches it.</summary>
    [Theory]
    [InlineData("brl")]
    [InlineData("BR")]
    [InlineData("")]
    public async Task An_account_currency_that_is_not_an_iso_code_is_refused(string currency)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("account-currency", cancellationToken);

        using var response = await user.Client.SendAsync(
            TransactionsFixtures.Post("/api/accounts", new
            {
                name = "Odd",
                type = "Checking",
                currency,
            }),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("currency", await TransactionsFixtures.ProblemFieldsAsync(response, cancellationToken));
    }

    [Fact]
    public async Task Editing_or_deleting_an_account_that_is_not_yours_is_a_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var owner = await factory.SignInNewUserAsync("account-owner", cancellationToken);
        var other = await factory.SignInNewUserAsync("account-other", cancellationToken);

        var accountId = await owner.CreateAccountAsync("Private", cancellationToken);

        using var edit = await other.Client.SendAsync(
            TransactionsFixtures.Put($"/api/accounts/{accountId}", new
            {
                name = "Hijacked",
                type = "Checking",
                currency = "BRL",
            }),
            cancellationToken);

        using var delete = await other.Client.SendAsync(
            TransactionsFixtures.Delete($"/api/accounts/{accountId}"), cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);
    }

    /// <summary>
    /// 005 amendment 3. The opening balance is how the dashboard total can match the
    /// bank's; it is set on create, changed on update, and kept when an update omits it.
    /// </summary>
    [Fact]
    public async Task The_opening_balance_is_set_on_create_and_changed_on_update()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("account-opening", cancellationToken);

        using var created = await user.Client.SendAsync(
            TransactionsFixtures.Post("/api/accounts", new
            {
                name = "Itaú",
                type = "Checking",
                currency = "BRL",
                openingBalance = 1000.50m,
            }),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var body = await created.Content.ReadFromJsonAsync<TransactionsFixtures.AccountItem>(cancellationToken);
        Assert.Equal(1000.50m, body!.OpeningBalance);

        // A card starts in debt, so a negative opening balance is ordinary.
        using var edited = await user.Client.SendAsync(
            TransactionsFixtures.Put($"/api/accounts/{body.Id}", new
            {
                name = "Itaú",
                type = "CreditCard",
                currency = "BRL",
                openingBalance = -250.75m,
            }),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, edited.StatusCode);

        using var renamed = await user.Client.SendAsync(
            TransactionsFixtures.Put($"/api/accounts/{body.Id}", new
            {
                name = "Itaú cartão",
                type = "CreditCard",
                currency = "BRL",
            }),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, renamed.StatusCode);

        var only = Assert.Single((await user.Client.GetFromJsonAsync<List<TransactionsFixtures.AccountItem>>(
            "/api/accounts", cancellationToken))!);

        Assert.Equal("Itaú cartão", only.Name);
        Assert.Equal(-250.75m, only.OpeningBalance);

        await using var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id);
        var stored = await context.Accounts.SingleAsync(cancellationToken);
        Assert.Equal(-250.75m, stored.OpeningBalance);
    }

    /// <summary>005 amendment 3: omitted on create means zero.</summary>
    [Fact]
    public async Task The_opening_balance_defaults_to_zero()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("account-opening-zero", cancellationToken);

        await user.CreateAccountAsync("Nubank", cancellationToken);

        var only = Assert.Single((await user.Client.GetFromJsonAsync<List<TransactionsFixtures.AccountItem>>(
            "/api/accounts", cancellationToken))!);

        Assert.Equal(0m, only.OpeningBalance);
    }

    /// <summary>
    /// Isolation: another user cannot set the opening balance of an account that is not
    /// theirs, and the owner still can, so the 404 is not a missing route.
    /// </summary>
    [Fact]
    public async Task Another_user_cannot_change_your_opening_balance()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var owner = await factory.SignInNewUserAsync("opening-owner", cancellationToken);
        var other = await factory.SignInNewUserAsync("opening-other", cancellationToken);

        var accountId = await owner.CreateAccountAsync("Private", cancellationToken);

        using var hijack = await other.Client.SendAsync(
            TransactionsFixtures.Put($"/api/accounts/{accountId}", new
            {
                name = "Private",
                type = "Checking",
                currency = "BRL",
                openingBalance = 999999m,
            }),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, hijack.StatusCode);

        using var own = await owner.Client.SendAsync(
            TransactionsFixtures.Put($"/api/accounts/{accountId}", new
            {
                name = "Private",
                type = "Checking",
                currency = "BRL",
                openingBalance = 10m,
            }),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, own.StatusCode);

        var only = Assert.Single((await owner.Client.GetFromJsonAsync<List<TransactionsFixtures.AccountItem>>(
            "/api/accounts", cancellationToken))!);

        Assert.Equal(10m, only.OpeningBalance);
    }
}
