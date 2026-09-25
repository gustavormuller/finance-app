using System.Globalization;
using System.Net;
using System.Net.Http.Json;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// Spec 013 integration tests 1–4: how much each category was used in a range, by
/// default the 12 months ending today.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class CategoryUsageTests(PostgresFixture postgres)
{
    private sealed record Usage(Guid CategoryId, int Count, decimal Total);

    private static readonly DateOnly Today = DateOnly.FromDateTime(DateTime.UtcNow);

    private static string Iso(DateOnly date) => date.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);

    /// <summary>Spec test 1.</summary>
    [Fact]
    public async Task Usage_counts_and_sums_the_last_12_months_and_leaves_older_ones_out()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("usage-default", cancellationToken);
        var accountId = await user.CreateAccountAsync("Conta", cancellationToken);
        var food = await user.CreateCategoryAsync("Mercado", cancellationToken);
        var fun = await user.CreateCategoryAsync("Cinema", cancellationToken);

        await PostAsync(user, accountId, food, -100.10m, Today.AddMonths(-1), cancellationToken);
        await PostAsync(user, accountId, food, -50.25m, Today, cancellationToken);
        await PostAsync(user, accountId, food, -999m, Today.AddMonths(-13), cancellationToken);
        await PostAsync(user, accountId, fun, -30m, Today.AddMonths(-11), cancellationToken);

        var usage = await GetAsync(user, "/api/categories/usage", cancellationToken);

        var foodUsage = Assert.Single(usage, entry => entry.CategoryId == food);
        Assert.Equal((2, -150.35m), (foodUsage.Count, foodUsage.Total));
        var funUsage = Assert.Single(usage, entry => entry.CategoryId == fun);
        Assert.Equal((1, -30m), (funUsage.Count, funUsage.Total));
    }

    /// <summary>Spec test 2.</summary>
    [Fact]
    public async Task From_and_to_narrow_the_range()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("usage-range", cancellationToken);
        var accountId = await user.CreateAccountAsync("Conta", cancellationToken);
        var food = await user.CreateCategoryAsync("Mercado", cancellationToken);

        await PostAsync(user, accountId, food, -10m, new DateOnly(2025, 3, 10), cancellationToken);
        await PostAsync(user, accountId, food, -20m, new DateOnly(2025, 4, 10), cancellationToken);
        await PostAsync(user, accountId, food, -40m, new DateOnly(2025, 5, 10), cancellationToken);

        var usage = await GetAsync(user, "/api/categories/usage?from=2025-04-01&to=2025-05-10", cancellationToken);

        var entry = Assert.Single(usage);
        Assert.Equal((food, 2, -60m), (entry.CategoryId, entry.Count, entry.Total));
    }

    /// <summary>Spec test 3.</summary>
    [Fact]
    public async Task Another_users_transactions_are_never_counted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var owner = await factory.SignInNewUserAsync("usage-owner", cancellationToken);
        var other = await factory.SignInNewUserAsync("usage-other", cancellationToken);
        var accountId = await owner.CreateAccountAsync("Conta", cancellationToken);
        var food = await owner.CreateCategoryAsync("Mercado", cancellationToken);
        await PostAsync(owner, accountId, food, -10m, Today, cancellationToken);

        Assert.Empty(await GetAsync(other, "/api/categories/usage", cancellationToken));
        Assert.Single(await GetAsync(owner, "/api/categories/usage", cancellationToken));
    }

    /// <summary>Spec test 4.</summary>
    [Fact]
    public async Task From_after_to_is_a_400_with_its_message()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("usage-bad", cancellationToken);

        using var response = await user.Client.GetAsync("/api/categories/usage?from=2025-06-01&to=2025-05-01", cancellationToken);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Contains("A data inicial deve ser anterior ou igual à data final.", await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static Task<Guid> PostAsync(SignedInUser user, Guid accountId, Guid categoryId, decimal amount, DateOnly date, CancellationToken cancellationToken) =>
        user.CreateTransactionAsync(TransactionsFixtures.TransactionBody(accountId, categoryId, amount, date: Iso(date)), cancellationToken);

    private static async Task<List<Usage>> GetAsync(SignedInUser user, string url, CancellationToken cancellationToken) =>
        (await user.Client.GetFromJsonAsync<List<Usage>>(url, cancellationToken))!;
}
