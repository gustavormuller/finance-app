using System.Globalization;
using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// Spec integration tests 4 (over HTTP), 7, 8, 9, and 12 to 15: the rules the
/// transaction endpoints enforce, and the shape of what they return.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class TransactionEndpointTests(PostgresFixture postgres)
{
    /// <summary>
    /// The HTTP half of spec integration test 4. The persistence half already showed
    /// the value survives PostgreSQL; this shows it survives JSON in both directions,
    /// which is the other place a float would destroy it.
    /// </summary>
    [Theory]
    [InlineData("1234567890.12")]
    [InlineData("-0.01")]
    [InlineData("-42.90")]
    [InlineData("3000.00")]
    public async Task Amount_round_trips_byte_identical_through_the_api(string literal)
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var amount = decimal.Parse(literal, System.Globalization.CultureInfo.InvariantCulture);

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("http-amount", cancellationToken);
        var accountId = await user.CreateAccountAsync("Nubank", cancellationToken);

        // The sign has to agree with the kind, so the category follows the amount.
        var categoryId = await user.CreateCategoryAsync(
            "Round trip",
            amount < 0 ? "Expense" : "Income",
            parentId: null,
            cancellationToken);

        var id = await user.CreateTransactionAsync(
            TransactionsFixtures.TransactionBody(accountId, categoryId, amount),
            cancellationToken);

        var page = await user.Client.GetFromJsonAsync<TransactionsFixtures.TransactionPage>(
            "/api/transactions", cancellationToken);

        var item = Assert.Single(page!.Items);
        Assert.Equal(id, item.Id);
        Assert.Equal(amount, item.Amount);

        // Read as raw text: a number that survived as a decimal is still written with
        // the two decimal places it was stored with. A float in the pipeline shows up
        // here as 1234567890.1200001 or as a dropped trailing zero.
        var body = await user.Client.GetStringAsync("/api/transactions", cancellationToken);

        using var document = JsonDocument.Parse(body);
        var serialised = document.RootElement
            .GetProperty("items")[0]
            .GetProperty("amount")
            .GetRawText();

        Assert.Equal(literal, serialised);
    }

    /// <summary>The response carries the names, so the list needs no second request.</summary>
    [Fact]
    public async Task A_transaction_comes_back_with_its_account_and_category_names()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("shape", cancellationToken);
        var accountId = await user.CreateAccountAsync("Nubank", cancellationToken);
        var categoryId = await user.CreateCategoryAsync("Groceries", cancellationToken);

        await user.CreateTransactionAsync(
            TransactionsFixtures.TransactionBody(accountId, categoryId),
            cancellationToken);

        var page = await user.Client.GetFromJsonAsync<TransactionsFixtures.TransactionPage>(
            "/api/transactions", cancellationToken);

        var item = Assert.Single(page!.Items);

        Assert.Equal("Nubank", item.AccountName);
        Assert.Equal("Groceries", item.CategoryName);
        Assert.Equal("BRL", item.Currency);
        Assert.Equal(new DateOnly(2026, 9, 13), item.Date);
        Assert.Equal("Supermarket", item.Description);
    }

    /// <summary>Spec integration test 7.</summary>
    [Fact]
    public async Task A_currency_other_than_the_accounts_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("currency", cancellationToken);
        var accountId = await user.CreateAccountAsync("Nubank", "Checking", "BRL", cancellationToken);
        var categoryId = await user.CreateCategoryAsync("Groceries", cancellationToken);

        using var response = await user.Client.SendAsync(
            TransactionsFixtures.Post(
                "/api/transactions",
                TransactionsFixtures.TransactionBody(accountId, categoryId, currency: "USD")),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("currency", await TransactionsFixtures.ProblemFieldsAsync(response, cancellationToken));
    }

    /// <summary>Spec integration test 8, both directions.</summary>
    [Theory]
    [InlineData("Income", 3000, false)]
    [InlineData("Income", -3000, true)]
    [InlineData("Expense", -1200, false)]
    [InlineData("Expense", 1200, true)]
    public async Task Sign_must_agree_with_the_category_kind(string kind, int amount, bool refused)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("sign", cancellationToken);
        var accountId = await user.CreateAccountAsync("Nubank", cancellationToken);
        var categoryId = await user.CreateCategoryAsync("Signed", kind, parentId: null, cancellationToken);

        using var response = await user.Client.SendAsync(
            TransactionsFixtures.Post(
                "/api/transactions",
                TransactionsFixtures.TransactionBody(accountId, categoryId, amount)),
            cancellationToken);

        if (!refused)
        {
            Assert.Equal(HttpStatusCode.Created, response.StatusCode);

            return;
        }

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("amount", await TransactionsFixtures.ProblemFieldsAsync(response, cancellationToken));
    }

    /// <summary>Spec integration test 9.</summary>
    [Fact]
    public async Task A_zero_amount_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("zero", cancellationToken);
        var accountId = await user.CreateAccountAsync("Nubank", cancellationToken);
        var categoryId = await user.CreateCategoryAsync("Groceries", cancellationToken);

        using var response = await user.Client.SendAsync(
            TransactionsFixtures.Post(
                "/api/transactions",
                TransactionsFixtures.TransactionBody(accountId, categoryId, 0m)),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("amount", await TransactionsFixtures.ProblemFieldsAsync(response, cancellationToken));
    }

    /// <summary>Spec rule 5, the sanity bound that exists to catch a parse error in 004.</summary>
    [Theory]
    [InlineData("1899-12-31")]
    [InlineData("2999-01-01")]
    public async Task A_date_outside_the_sanity_bounds_is_refused(string date)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("date-bounds", cancellationToken);
        var accountId = await user.CreateAccountAsync("Nubank", cancellationToken);
        var categoryId = await user.CreateCategoryAsync("Groceries", cancellationToken);

        using var response = await user.Client.SendAsync(
            TransactionsFixtures.Post(
                "/api/transactions",
                TransactionsFixtures.TransactionBody(accountId, categoryId, date: date)),
            cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("date", await TransactionsFixtures.ProblemFieldsAsync(response, cancellationToken));
    }

    /// <summary>
    /// Rules 1 to 5 in the order they are decided, each refusal with its fields and exact
    /// pt-BR words: the ids first (both at once), then the description, the currency, and
    /// last the amount, sign and date together.
    /// </summary>
    [Fact]
    public async Task Each_transaction_refusal_keeps_its_fields_and_its_words()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("transaction-words", cancellationToken);
        var accountId = await user.CreateAccountAsync("Nubank", cancellationToken);
        var categoryId = await user.CreateCategoryAsync("Groceries", cancellationToken);
        var latest = DateOnly.FromDateTime(DateTime.UtcNow).AddYears(1).ToString("dd/MM/yyyy", CultureInfo.InvariantCulture);

        async Task Refused(object body, Dictionary<string, string[]> expected)
        {
            using var response = await user.Client.SendAsync(TransactionsFixtures.Post("/api/transactions", body), cancellationToken);
            using var problem = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));

            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
            Assert.Equal(
                expected,
                problem.RootElement.GetProperty("errors").EnumerateObject().ToDictionary(
                    field => field.Name,
                    field => field.Value.EnumerateArray().Select(message => message.GetString()!).ToArray()));
        }

        await Refused(
            TransactionsFixtures.TransactionBody(Guid.NewGuid(), Guid.NewGuid(), description: " "),
            new() { ["accountId"] = ["Conta não encontrada."], ["categoryId"] = ["Categoria não encontrada."] });
        await Refused(
            TransactionsFixtures.TransactionBody(accountId, categoryId, 0m, currency: "usd", description: " "),
            new() { ["description"] = ["A descrição é obrigatória."] });
        await Refused(
            TransactionsFixtures.TransactionBody(accountId, categoryId, 0m, currency: "usd"),
            new() { ["currency"] = ["A moeda deve ser um código ISO 4217 de três letras maiúsculas."] });
        await Refused(
            TransactionsFixtures.TransactionBody(accountId, categoryId, 0m, currency: "USD"),
            new() { ["currency"] = ["'Nubank' está em BRL, então o lançamento não pode estar em USD."] });
        await Refused(
            TransactionsFixtures.TransactionBody(accountId, categoryId, 0.001m, date: "1899-12-31"),
            new() { ["amount"] = ["O valor não pode ser zero."], ["date"] = [$"A data deve estar entre 01/01/1900 e {latest}."] });
        await Refused(
            TransactionsFixtures.TransactionBody(accountId, categoryId, 12m),
            new() { ["amount"] = ["Uma categoria de despesa exige um valor negativo."] });
    }

    /// <summary>Spec integration test 12. Both ends of the range are inclusive.</summary>
    [Fact]
    public async Task The_date_range_filter_includes_both_boundaries()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("range", cancellationToken);
        var accountId = await user.CreateAccountAsync("Nubank", cancellationToken);
        var categoryId = await user.CreateCategoryAsync("Groceries", cancellationToken);

        foreach (var day in new[] { "2026-03-09", "2026-03-10", "2026-03-15", "2026-03-20", "2026-03-21" })
        {
            await user.CreateTransactionAsync(
                TransactionsFixtures.TransactionBody(accountId, categoryId, date: day, description: day),
                cancellationToken);
        }

        var page = await user.Client.GetFromJsonAsync<TransactionsFixtures.TransactionPage>(
            "/api/transactions?from=2026-03-10&to=2026-03-20", cancellationToken);

        Assert.Equal(3, page!.Total);

        Assert.Equal(
            ["2026-03-20", "2026-03-15", "2026-03-10"],
            page.Items.Select(item => item.Description));
    }

    /// <summary>Spec integration test 13.</summary>
    [Fact]
    public async Task Page_two_of_a_hundred_and_twenty_rows_returns_rows_fifty_one_to_a_hundred()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("paging", cancellationToken);
        var accountId = await user.CreateAccountAsync("Nubank", cancellationToken);
        var categoryId = await user.CreateCategoryAsync("Groceries", cancellationToken);

        // Descending dates, so row N of the ordered result is the Nth one created.
        var start = new DateOnly(2026, 6, 1);

        for (var index = 0; index < 120; index++)
        {
            await user.CreateTransactionAsync(
                TransactionsFixtures.TransactionBody(
                    accountId,
                    categoryId,
                    date: start.AddDays(-index).ToString("yyyy-MM-dd"),
                    description: $"row {index + 1}"),
                cancellationToken);
        }

        var page = await user.Client.GetFromJsonAsync<TransactionsFixtures.TransactionPage>(
            "/api/transactions?page=2&pageSize=50", cancellationToken);

        Assert.Equal(120, page!.Total);
        Assert.Equal(2, page.Page);
        Assert.Equal(50, page.PageSize);
        Assert.Equal(50, page.Items.Count);
        Assert.Equal("row 51", page.Items[0].Description);
        Assert.Equal("row 100", page.Items[^1].Description);
    }

    /// <summary>Spec integration test 14.</summary>
    [Fact]
    public async Task A_page_size_over_two_hundred_is_clamped()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("clamp", cancellationToken);

        var page = await user.Client.GetFromJsonAsync<TransactionsFixtures.TransactionPage>(
            "/api/transactions?pageSize=500", cancellationToken);

        // Clamped rather than refused: an oversized page is a caller being greedy, not
        // a caller being wrong, and 200 rows is still an answer to their question.
        Assert.Equal(200, page!.PageSize);
    }

    /// <summary>
    /// Spec integration test 15. Two rows on the same day fall back to insertion
    /// order, newest first, so the list does not reshuffle between requests.
    /// </summary>
    [Fact]
    public async Task Same_day_rows_are_ordered_by_creation_newest_first()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("ordering", cancellationToken);
        var accountId = await user.CreateAccountAsync("Nubank", cancellationToken);
        var categoryId = await user.CreateCategoryAsync("Groceries", cancellationToken);

        await user.CreateTransactionAsync(
            TransactionsFixtures.TransactionBody(accountId, categoryId, date: "2026-05-02", description: "older day"),
            cancellationToken);

        await user.CreateTransactionAsync(
            TransactionsFixtures.TransactionBody(accountId, categoryId, date: "2026-05-03", description: "first"),
            cancellationToken);

        await user.CreateTransactionAsync(
            TransactionsFixtures.TransactionBody(accountId, categoryId, date: "2026-05-03", description: "second"),
            cancellationToken);

        var page = await user.Client.GetFromJsonAsync<TransactionsFixtures.TransactionPage>(
            "/api/transactions", cancellationToken);

        Assert.Equal(
            ["second", "first", "older day"],
            page!.Items.Select(item => item.Description));
    }

    /// <summary>Filtering by account and by category, which the UI's filter bar needs.</summary>
    [Fact]
    public async Task The_list_can_be_narrowed_to_one_account_or_one_category()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("narrow", cancellationToken);
        var nubank = await user.CreateAccountAsync("Nubank", cancellationToken);
        var inter = await user.CreateAccountAsync("Inter", cancellationToken);
        var groceries = await user.CreateCategoryAsync("Groceries", cancellationToken);
        var travel = await user.CreateCategoryAsync("Travel", cancellationToken);

        await user.CreateTransactionAsync(
            TransactionsFixtures.TransactionBody(nubank, groceries, description: "nubank groceries"),
            cancellationToken);

        await user.CreateTransactionAsync(
            TransactionsFixtures.TransactionBody(inter, travel, description: "inter travel"),
            cancellationToken);

        var byAccount = await user.Client.GetFromJsonAsync<TransactionsFixtures.TransactionPage>(
            $"/api/transactions?accountId={nubank}", cancellationToken);

        var byCategory = await user.Client.GetFromJsonAsync<TransactionsFixtures.TransactionPage>(
            $"/api/transactions?categoryId={travel}", cancellationToken);

        Assert.Equal("nubank groceries", Assert.Single(byAccount!.Items).Description);
        Assert.Equal("inter travel", Assert.Single(byCategory!.Items).Description);
    }

    [Fact]
    public async Task A_transaction_can_be_edited_and_deleted_by_its_owner()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("edit", cancellationToken);
        var accountId = await user.CreateAccountAsync("Nubank", cancellationToken);
        var categoryId = await user.CreateCategoryAsync("Groceries", cancellationToken);

        var id = await user.CreateTransactionAsync(
            TransactionsFixtures.TransactionBody(accountId, categoryId),
            cancellationToken);

        using var edit = await user.Client.SendAsync(
            TransactionsFixtures.Put(
                $"/api/transactions/{id}",
                TransactionsFixtures.TransactionBody(
                    accountId, categoryId, -55.55m, description: "Corrected")),
            cancellationToken);

        Assert.Equal(HttpStatusCode.OK, edit.StatusCode);

        var edited = await edit.Content.ReadFromJsonAsync<TransactionsFixtures.TransactionItem>(cancellationToken);
        Assert.Equal(-55.55m, edited!.Amount);
        Assert.Equal("Corrected", edited.Description);

        using var delete = await user.Client.SendAsync(
            TransactionsFixtures.Delete($"/api/transactions/{id}"), cancellationToken);

        Assert.Equal(HttpStatusCode.NoContent, delete.StatusCode);

        var page = await user.Client.GetFromJsonAsync<TransactionsFixtures.TransactionPage>(
            "/api/transactions", cancellationToken);

        Assert.Equal(0, page!.Total);
    }
}
