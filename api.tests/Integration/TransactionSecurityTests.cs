using System.Net;
using System.Net.Http.Json;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// Spec integration tests 1, 2 and 3 over HTTP: what happens when a perfectly
/// well-formed request from one user names another user's row.
/// </summary>
/// <remarks>
/// The spec calls rule 4 the security-relevant one, and these are its tests. The
/// answer is always 400 or 404 and never 403: a 403 would confirm the id exists,
/// which is itself a leak. A row belonging to someone else has to behave exactly as
/// if it were never written.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class TransactionSecurityTests(PostgresFixture postgres)
{
    /// <summary>Spec integration test 1, over the three list endpoints.</summary>
    [Fact]
    public async Task Everything_one_user_creates_is_absent_from_every_list_the_other_reads()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var userA = await factory.SignInNewUserAsync("http-iso-a", cancellationToken);
        var userB = await factory.SignInNewUserAsync("http-iso-b", cancellationToken);

        var world = await CreateAccountCategoryAndTransaction(userA, "A's bank", cancellationToken);

        var accounts = await userB.Client.GetFromJsonAsync<List<TransactionsFixtures.AccountItem>>(
            "/api/accounts", cancellationToken);

        var categories = await userB.Client.GetFromJsonAsync<List<TransactionsFixtures.CategoryItem>>(
            "/api/categories", cancellationToken);

        var transactions = await userB.Client.GetFromJsonAsync<TransactionsFixtures.TransactionPage>(
            "/api/transactions", cancellationToken);

        Assert.Empty(accounts!);
        Assert.Empty(transactions!.Items);
        Assert.Equal(0, transactions.Total);

        // Categories are the one list that is not empty, because B has their own
        // seeded ones. Asserting "empty" here would also pass if the filter were
        // hiding every row from everybody.
        Assert.DoesNotContain(categories!, category => category.Id == world.CategoryId);
        Assert.NotEmpty(categories!);

        // And A still sees all of it: a filter that hides rows from their owner is
        // just as broken.
        var mine = await userA.Client.GetFromJsonAsync<TransactionsFixtures.TransactionPage>("/api/transactions", cancellationToken);
        Assert.Equal(1, mine!.Total);
    }

    /// <summary>
    /// Spec integration test 2. The case the spec singles out: a valid-looking request
    /// from B naming A's account. It must be refused as a bad field rather than as a
    /// forbidden row, and it must write nothing at all.
    /// </summary>
    [Fact]
    public async Task Posting_a_transaction_against_another_users_account_is_refused_and_writes_nothing()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var userA = await factory.SignInNewUserAsync("cross-post-a", cancellationToken);
        var userB = await factory.SignInNewUserAsync("cross-post-b", cancellationToken);

        var owned = await CreateAccountCategoryAndTransaction(userA, "A's only bank", cancellationToken);
        var intruderCategory = await userB.CreateCategoryAsync("B's category", cancellationToken);

        var before = await userA.Client.GetFromJsonAsync<TransactionsFixtures.TransactionPage>("/api/transactions", cancellationToken);
        Assert.Equal(1, before!.Total);

        using var response = await userB.Client.SendAsync(
            TransactionsFixtures.Post("/api/transactions", new
            {
                accountId = owned.AccountId,
                categoryId = intruderCategory,
                amount = -10.00m,
                currency = "BRL",
                date = "2026-09-13",
                description = "Not mine to post",
            }),
            cancellationToken);

        // 400, not 403: a 403 would confirm the account id exists.
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("accountId", await TransactionsFixtures.ProblemFieldsAsync(response, cancellationToken));

        // Nothing written, from A's point of view...
        var after = await userA.Client.GetFromJsonAsync<TransactionsFixtures.TransactionPage>("/api/transactions", cancellationToken);
        Assert.Equal(1, after!.Total);
        Assert.Equal(before.Items[0].Id, after.Items[0].Id);

        // ...nor from B's...
        var intruderView = await userB.Client.GetFromJsonAsync<TransactionsFixtures.TransactionPage>("/api/transactions", cancellationToken);
        Assert.Equal(0, intruderView!.Total);

        // ...nor in the table itself, read past the query filter entirely. A row
        // written with the wrong UserId would be invisible to both clients above and
        // would still be a breach.
        await using var unfiltered = TransactionsFixtures.ContextFor(postgres.ConnectionString, userA.Id);

        Assert.Equal(
            1,
            await unfiltered.Transactions.IgnoreQueryFilters()
                .CountAsync(entity => entity.AccountId == owned.AccountId, cancellationToken));
    }

    /// <summary>Spec integration test 3.</summary>
    [Fact]
    public async Task Editing_or_deleting_another_users_transaction_is_a_404()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var userA = await factory.SignInNewUserAsync("cross-edit-a", cancellationToken);
        var userB = await factory.SignInNewUserAsync("cross-edit-b", cancellationToken);

        var owned = await CreateAccountCategoryAndTransaction(userA, "A's bank again", cancellationToken);
        var intruderAccount = await userB.CreateAccountAsync("B's bank", cancellationToken);
        var intruderCategory = await userB.CreateCategoryAsync("B's category", cancellationToken);

        using var edit = await userB.Client.SendAsync(
            TransactionsFixtures.Put($"/api/transactions/{owned.TransactionId}", new
            {
                accountId = intruderAccount,
                categoryId = intruderCategory,
                amount = -1.00m,
                currency = "BRL",
                date = "2026-09-13",
                description = "Rewritten by someone else",
            }),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, edit.StatusCode);

        using var delete = await userB.Client.SendAsync(
            TransactionsFixtures.Delete($"/api/transactions/{owned.TransactionId}"),
            cancellationToken);

        Assert.Equal(HttpStatusCode.NotFound, delete.StatusCode);

        // Untouched, and still A's.
        var mine = await userA.Client.GetFromJsonAsync<TransactionsFixtures.TransactionPage>("/api/transactions", cancellationToken);
        Assert.Equal(1, mine!.Total);
        Assert.Equal("Supermarket", mine.Items[0].Description);
        Assert.Equal(-42.90m, mine.Items[0].Amount);
    }

    /// <summary>Every route in 003 is behind the session, including the reads.</summary>
    [Theory]
    [InlineData("/api/accounts")]
    [InlineData("/api/categories")]
    [InlineData("/api/transactions")]
    public async Task Listing_without_a_session_is_401(string url)
    {
        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        using var response = await client.GetAsync(url, TestContext.Current.CancellationToken);

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    private static async Task<(Guid AccountId, Guid CategoryId, Guid TransactionId)>
        CreateAccountCategoryAndTransaction(
            SignedInUser user,
            string accountName,
            CancellationToken cancellationToken)
    {
        var accountId = await user.CreateAccountAsync(accountName, cancellationToken);
        var categoryId = await user.CreateCategoryAsync($"{accountName} category", cancellationToken);

        var transactionId = await user.CreateTransactionAsync(
            TransactionsFixtures.TransactionBody(accountId, categoryId),
            cancellationToken);

        return (accountId, categoryId, transactionId);
    }
}
