using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finance.Api.Domain.Transactions;
using static Finance.Api.Tests.Integration.DashboardFixtures;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 005 spec integration tests 5 and 7 to 12: isolation, balances and transfers, plus
/// the parameter validation of the three dashboard endpoints.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class DashboardEndpointTests(PostgresFixture postgres)
{
    /// <summary>
    /// Spec integration test 5. Dapper bypasses the query filter, so this is the test
    /// that notices a query forgetting its <c>UserId</c> predicate.
    /// </summary>
    [Fact]
    public async Task Another_users_data_never_reaches_the_dashboard()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var alice = await factory.SignInNewUserAsync("dash-alice", cancellationToken);
        var bob = await factory.SignInNewUserAsync("dash-bob", cancellationToken);

        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, alice.Id))
        {
            var account = await context.AddAccountAsync(alice.Id, "Nubank", openingBalance: 500m);
            var food = await context.AddCategoryAsync(alice.Id, "Mercado");
            var salary = await context.AddCategoryAsync(alice.Id, "Bolsa", CategoryKind.Income);

            await context.AddTransactionAsync(account, food, -120.35m, ThisMonth);
            await context.AddTransactionAsync(account, salary, 3000m, ThisMonth.AddMonths(-2));
        }

        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, bob.Id))
        {
            await context.AddAccountAsync(bob.Id, "Nubank");
        }

        var summary = await bob.Client.GetFromJsonAsync<Summary>("/api/dashboard/summary", cancellationToken);

        var only = Assert.Single(summary!.Balances);
        Assert.Equal(0m, only.Balance);
        Assert.Equal(0m, summary.Total);
        Assert.Equal(new MonthItem(0m, 0m, 0m), summary.Month);

        var monthly = await bob.Client.GetFromJsonAsync<List<MonthlyItem>>(
            "/api/dashboard/monthly", cancellationToken);

        Assert.Equal(12, monthly!.Count);
        Assert.All(monthly, month => Assert.Equal((0m, 0m), (month.Income, month.Expense)));

        foreach (var kind in new[] { "Expense", "Income" })
        {
            var byCategory = await bob.Client.GetFromJsonAsync<List<CategoryItem>>(
                $"/api/dashboard/by-category?month={ThisMonth.Key()}&kind={kind}", cancellationToken);

            Assert.Empty(byCategory!);
        }
    }

    /// <summary>Spec integration tests 7 and 8.</summary>
    [Fact]
    public async Task A_balance_is_the_opening_balance_plus_the_transactions()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("dash-balance", cancellationToken);

        Guid moving, idle;

        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            var account = await context.AddAccountAsync(user.Id, "Itaú", openingBalance: 1000m);
            var quiet = await context.AddAccountAsync(user.Id, "Poupança", openingBalance: 250.10m);
            var food = await context.AddCategoryAsync(user.Id, "Mercado");
            var refund = await context.AddCategoryAsync(user.Id, "Reembolso", CategoryKind.Income);

            await context.AddTransactionAsync(account, food, -200m, ThisMonth);
            await context.AddTransactionAsync(account, refund, 50m, ThisMonth.AddMonths(-1));

            (moving, idle) = (account.Id, quiet.Id);
        }

        var summary = await user.Client.GetFromJsonAsync<Summary>("/api/dashboard/summary", cancellationToken);

        Assert.Equal(850m, summary!.Balances.Single(row => row.AccountId == moving).Balance);
        Assert.Equal(250.10m, summary.Balances.Single(row => row.AccountId == idle).Balance);
        Assert.Equal(1100.10m, summary.Total);
    }

    /// <summary>Spec integration test 9.</summary>
    [Fact]
    public async Task A_foreign_currency_account_is_listed_but_left_out_of_the_total()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("dash-currency", cancellationToken);

        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            await context.AddAccountAsync(user.Id, "Itaú", openingBalance: 100m);
            await context.AddAccountAsync(user.Id, "Wise", openingBalance: 40m, currency: "USD");
        }

        var summary = await user.Client.GetFromJsonAsync<Summary>("/api/dashboard/summary", cancellationToken);

        var dollars = summary!.Balances.Single(row => row.Currency == "USD");
        Assert.Equal(40m, dollars.Balance);
        Assert.True(dollars.ExcludedFromTotal);
        Assert.False(summary.Balances.Single(row => row.Currency == "BRL").ExcludedFromTotal);
        Assert.Equal(100m, summary.Total);
    }

    /// <summary>
    /// Spec integration test 10: paying the card bill moves both balances and is
    /// neither income nor expense.
    /// </summary>
    [Fact]
    public async Task A_transfer_moves_balances_but_not_the_month_totals()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("dash-transfer", cancellationToken);

        Guid checkingId, cardId;

        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            var checking = await context.AddAccountAsync(user.Id, "Itaú", openingBalance: 5000m);
            var card = await context.AddAccountAsync(
                user.Id, "Cartão", openingBalance: -3000m, type: AccountType.CreditCard);
            var food = await context.AddCategoryAsync(user.Id, "Mercado");
            var transfer = context.Categories.Single(category => category.Name == DefaultCategories.TransferName);

            await context.AddTransactionAsync(checking, food, -100m, ThisMonth);
            await context.AddTransactionAsync(checking, transfer, -3000m, ThisMonth);
            await context.AddTransactionAsync(card, transfer, 3000m, ThisMonth);

            (checkingId, cardId) = (checking.Id, card.Id);
        }

        var summary = await user.Client.GetFromJsonAsync<Summary>("/api/dashboard/summary", cancellationToken);

        Assert.Equal(1900m, summary!.Balances.Single(row => row.AccountId == checkingId).Balance);
        Assert.Equal(0m, summary.Balances.Single(row => row.AccountId == cardId).Balance);
        Assert.Equal(new MonthItem(0m, -100m, -100m), summary.Month);

        var monthly = await user.Client.GetFromJsonAsync<List<MonthlyItem>>(
            "/api/dashboard/monthly", cancellationToken);

        Assert.Equal(new MonthlyItem(ThisMonth.Key(), 0m, -100m), monthly![^1]);
    }

    /// <summary>Spec integration test 11.</summary>
    [Fact]
    public async Task By_category_never_returns_a_transfer_category()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("dash-transfer-category", cancellationToken);

        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            var account = await context.AddAccountAsync(user.Id, "Itaú");
            var food = await context.AddCategoryAsync(user.Id, "Mercado");
            var transfer = context.Categories.Single(category => category.Name == DefaultCategories.TransferName);

            await context.AddTransactionAsync(account, food, -80m, ThisMonth);
            await context.AddTransactionAsync(account, transfer, -500m, ThisMonth);
            await context.AddTransactionAsync(account, transfer, 700m, ThisMonth);
        }

        foreach (var kind in new[] { "Expense", "Income" })
        {
            var rows = await user.Client.GetFromJsonAsync<List<CategoryItem>>(
                $"/api/dashboard/by-category?kind={kind}", cancellationToken);

            Assert.DoesNotContain(rows!, row => row.Name == DefaultCategories.TransferName);
        }
    }

    /// <summary>Spec integration test 12, and the other parameters the endpoints refuse.</summary>
    [Theory]
    [InlineData("/api/dashboard/by-category?kind=Transfer", "kind")]
    [InlineData("/api/dashboard/by-category?kind=2", "kind")]
    [InlineData("/api/dashboard/by-category?kind=Savings", "kind")]
    [InlineData("/api/dashboard/by-category?month=2026-13", "month")]
    [InlineData("/api/dashboard/summary?month=2026-9", "month")]
    [InlineData("/api/dashboard/summary?month=setembro", "month")]
    [InlineData("/api/dashboard/monthly?months=doze", "months")]
    public async Task An_invalid_parameter_is_a_400_in_portuguese(string url, string field)
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("dash-invalid", cancellationToken);

        using var response = await user.Client.GetAsync(url, cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

        using var document = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
        var message = document.RootElement.GetProperty("errors").GetProperty(field)[0].GetString()!;

        // Rendered verbatim by the web; the word for "month" or "type" is enough to
        // tell a pt-BR message from the framework's English default.
        Assert.Matches("mês|meses|tipo", message);
    }

    [Fact]
    public async Task The_dashboard_requires_a_session()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        using var client = factory.CreateApiClient();

        foreach (var url in new[] { "/api/dashboard/summary", "/api/dashboard/monthly", "/api/dashboard/by-category" })
        {
            using var response = await client.GetAsync(url, cancellationToken);

            Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        }
    }
}
