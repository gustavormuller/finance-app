using System.Net;
using System.Net.Http.Json;

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
        // body has to carry one.
        var reason = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("transaction", reason, StringComparison.OrdinalIgnoreCase);

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
}
