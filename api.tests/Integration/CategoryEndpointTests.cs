using System.Net;
using System.Net.Http.Json;
using Finance.Api.Domain.Transactions;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// The categories resource, plus spec integration tests 10, 11 and the HTTP half of
/// 17 — the rules that keep the tree exactly two levels deep.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class CategoryEndpointTests(PostgresFixture postgres)
{
    [Fact]
    public async Task The_list_is_flat_and_carries_parent_ids()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("categories", cancellationToken);

        var parentId = await user.CreateCategoryAsync("Groceries", cancellationToken);
        var childId = await user.CreateCategoryAsync("Supermarket", "Expense", parentId, cancellationToken);

        var categories = await user.Client.GetFromJsonAsync<List<TransactionsFixtures.CategoryItem>>(
            "/api/categories", cancellationToken);

        // The seeded ones plus the two created here, flat, with the hierarchy
        // expressed by parentId rather than by nesting.
        Assert.Equal(DefaultCategories.All.Count + 2, categories!.Count);

        var child = Assert.Single(categories, category => category.Id == childId);
        Assert.Equal(parentId, child.ParentId);
        Assert.Equal("Expense", child.Kind);

        Assert.Null(Assert.Single(categories, category => category.Id == parentId).ParentId);
    }

    /// <summary>Spec integration test 10, and spec rule 6.</summary>
    [Fact]
    public async Task A_grandchild_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("grandchild", cancellationToken);

        var parentId = await user.CreateCategoryAsync("Groceries", cancellationToken);
        var childId = await user.CreateCategoryAsync("Supermarket", "Expense", parentId, cancellationToken);

        using var response = await user.Client.SendAsync(
            TransactionsFixtures.Post("/api/categories", new
            {
                name = "Dairy aisle",
                kind = "Expense",
                parentId = childId,
            }),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("parentId", await TransactionsFixtures.ProblemFieldsAsync(response, cancellationToken));
    }

    /// <summary>Spec integration test 11.</summary>
    [Fact]
    public async Task A_child_whose_kind_differs_from_its_parent_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("kind-mismatch", cancellationToken);

        var parentId = await user.CreateCategoryAsync("Groceries", "Expense", null, cancellationToken);

        using var response = await user.Client.SendAsync(
            TransactionsFixtures.Post("/api/categories", new
            {
                name = "Cashback",
                kind = "Income",
                parentId,
            }),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("kind", await TransactionsFixtures.ProblemFieldsAsync(response, cancellationToken));
    }

    /// <summary>
    /// Spec rule 4 for the parent reference: another user's category must behave as
    /// if it did not exist, which makes this a 400 on the field rather than a 403.
    /// </summary>
    [Fact]
    public async Task A_parent_belonging_to_another_user_is_refused_as_a_bad_field()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var owner = await factory.SignInNewUserAsync("parent-owner", cancellationToken);
        var other = await factory.SignInNewUserAsync("parent-other", cancellationToken);

        var parentId = await owner.CreateCategoryAsync("Private parent", cancellationToken);

        using var response = await other.Client.SendAsync(
            TransactionsFixtures.Post("/api/categories", new
            {
                name = "Sneaky child",
                kind = "Expense",
                parentId,
            }),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("parentId", await TransactionsFixtures.ProblemFieldsAsync(response, cancellationToken));
    }

    /// <summary>Spec integration test 17 over HTTP.</summary>
    [Fact]
    public async Task A_category_with_children_cannot_be_deleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("category-409", cancellationToken);

        var parentId = await user.CreateCategoryAsync("Groceries", cancellationToken);
        await user.CreateCategoryAsync("Supermarket", "Expense", parentId, cancellationToken);

        using var response = await user.Client.SendAsync(
            TransactionsFixtures.Delete($"/api/categories/{parentId}"), cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var reason = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("subcategoria", reason, StringComparison.OrdinalIgnoreCase);

        var categories = await user.Client.GetFromJsonAsync<List<TransactionsFixtures.CategoryItem>>(
            "/api/categories", cancellationToken);

        Assert.Contains(categories!, category => category.Id == parentId);
    }

    [Fact]
    public async Task A_category_with_transactions_cannot_be_deleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("category-referenced", cancellationToken);

        var accountId = await user.CreateAccountAsync("Nubank", cancellationToken);
        var categoryId = await user.CreateCategoryAsync("Groceries", cancellationToken);

        await user.CreateTransactionAsync(
            TransactionsFixtures.TransactionBody(accountId, categoryId),
            cancellationToken);

        using var response = await user.Client.SendAsync(
            TransactionsFixtures.Delete($"/api/categories/{categoryId}"), cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

        var reason = await response.Content.ReadAsStringAsync(cancellationToken);
        Assert.Contains("lançamento", reason, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task A_category_with_nothing_behind_it_is_deleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("category-delete", cancellationToken);

        var categoryId = await user.CreateCategoryAsync("Disposable", cancellationToken);

        using var response = await user.Client.SendAsync(
            TransactionsFixtures.Delete($"/api/categories/{categoryId}"), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
    }

    /// <summary>
    /// The gap the NULLS NOT DISTINCT index closed, asserted from the outside: the
    /// seeded categories are all top level, so this is the collision a user would hit
    /// first.
    /// </summary>
    [Fact]
    public async Task A_second_top_level_category_cannot_reuse_a_seeded_name()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("category-dupe", cancellationToken);

        using var response = await user.Client.SendAsync(
            TransactionsFixtures.Post("/api/categories", new
            {
                name = "Alimentação",
                kind = "Expense",
                parentId = (Guid?)null,
            }),
            cancellationToken);

        Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    }

    /// <summary>
    /// A category that already has children cannot become a child itself — the other
    /// direction of spec rule 6, which only an edit can reach.
    /// </summary>
    [Fact]
    public async Task A_parent_cannot_be_edited_into_a_child()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("reparent", cancellationToken);

        var parentId = await user.CreateCategoryAsync("Groceries", cancellationToken);
        await user.CreateCategoryAsync("Supermarket", "Expense", parentId, cancellationToken);
        var otherId = await user.CreateCategoryAsync("Travel", cancellationToken);

        using var response = await user.Client.SendAsync(
            TransactionsFixtures.Put($"/api/categories/{parentId}", new
            {
                name = "Groceries",
                kind = "Expense",
                parentId = otherId,
            }),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("parentId", await TransactionsFixtures.ProblemFieldsAsync(response, cancellationToken));
    }
}
