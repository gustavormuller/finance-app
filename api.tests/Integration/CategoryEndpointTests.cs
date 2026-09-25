using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
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
    /// Every refusal the tree and delete rules give, with its status and its exact pt-BR
    /// text: the screen renders them verbatim, so the words are part of the contract.
    /// </summary>
    [Fact]
    public async Task Each_category_refusal_keeps_its_status_and_its_words()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("category-words", cancellationToken);

        var parentId = await user.CreateCategoryAsync("Groceries", cancellationToken);
        var childId = await user.CreateCategoryAsync("Supermarket", "Expense", parentId, cancellationToken);
        var travelId = await user.CreateCategoryAsync("Travel", cancellationToken);
        var accountId = await user.CreateAccountAsync("Nubank", cancellationToken);
        await user.CreateTransactionAsync(TransactionsFixtures.TransactionBody(accountId, travelId), cancellationToken);

        async Task Refused(HttpRequestMessage request, HttpStatusCode status, string field, string words)
        {
            using var response = await user.Client.SendAsync(request, cancellationToken);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

            Assert.Equal(status, response.StatusCode);
            Assert.Equal(
                words,
                field.Length == 0
                    ? problem.RootElement.GetProperty("detail").GetString()
                    : problem.RootElement.GetProperty("errors").GetProperty(field)[0].GetString());
        }

        HttpRequestMessage Post(string name, string kind, Guid? parent) =>
            TransactionsFixtures.Post("/api/categories", new { name, kind, parentId = parent });

        HttpRequestMessage Put(Guid id, string name, Guid? parent) =>
            TransactionsFixtures.Put($"/api/categories/{id}", new { name, kind = "Expense", parentId = parent });

        await Refused(Post(" ", "Expense", null), HttpStatusCode.BadRequest, "name", "O nome é obrigatório.");
        await Refused(Post("Orphan", "Expense", Guid.NewGuid()), HttpStatusCode.BadRequest, "parentId", "Categoria não encontrada.");
        await Refused(
            Post("Dairy aisle", "Expense", childId),
            HttpStatusCode.BadRequest,
            "parentId",
            "As categorias têm no máximo dois níveis, e essa já é uma subcategoria.");
        await Refused(
            Post("Cashback", "Income", parentId),
            HttpStatusCode.BadRequest,
            "kind",
            "Uma subcategoria de 'Groceries' precisa ser do mesmo tipo que ela.");
        await Refused(Put(travelId, "Travel", travelId), HttpStatusCode.BadRequest, "parentId", "Uma categoria não pode ser mãe de si mesma.");
        await Refused(
            Put(parentId, "Groceries", travelId),
            HttpStatusCode.BadRequest,
            "parentId",
            "Essa categoria tem subcategorias, então não pode virar subcategoria.");
        await Refused(
            Post("Supermarket", "Expense", parentId),
            HttpStatusCode.Conflict,
            "",
            "Já existe uma categoria chamada 'Supermarket' neste nível.");
        await Refused(
            TransactionsFixtures.Delete($"/api/categories/{parentId}"),
            HttpStatusCode.Conflict,
            "",
            "'Groceries' ainda tem 1 subcategoria(s). Exclua-as antes.");
        await Refused(
            TransactionsFixtures.Delete($"/api/categories/{travelId}"),
            HttpStatusCode.Conflict,
            "",
            "'Travel' ainda tem 1 lançamento(s). Recategorize-os ou exclua-os antes.");
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
