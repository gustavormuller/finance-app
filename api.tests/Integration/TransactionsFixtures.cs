using System.Net.Http.Json;
using System.Text.Json;
using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Finance.Api.Tests.Integration;

/// <summary>A user that exists in the database, and a client holding their session.</summary>
internal sealed record SignedInUser(HttpClient Client, Guid Id);

/// <summary>
/// Shared setup for 003's persistence tests. They work against
/// <see cref="AppDbContext"/> directly rather than over HTTP, because the endpoints
/// do not exist yet and the guarantees under test — the query filter, the column
/// types, the foreign keys — are the storage layer's, not the API's.
/// </summary>
internal static class TransactionsFixtures
{
    private sealed record MeResponse(Guid Id);

    /// <summary>
    /// Creates a user through the real 002 sign-in path and returns their id, so
    /// tests can open a context as them.
    /// </summary>
    public static async Task<SignedInUser> SignInNewUserAsync(
        this IdentityApiFactory factory,
        string prefix,
        CancellationToken cancellationToken)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@example.com";
        var client = await factory.CreateSignedInClientAsync(email, displayName: null, cancellationToken);

        var me = await client.GetFromJsonAsync<MeResponse>("/api/auth/me", cancellationToken);

        return new SignedInUser(client, me!.Id);
    }

    /// <summary>
    /// A context filtered as <paramref name="userId"/>. Separate instances rather than
    /// the host's scoped one, so a test can hold two users' views of the same database
    /// at once.
    /// </summary>
    public static AppDbContext ContextFor(string connectionString, Guid? userId) =>
        new(
            new DbContextOptionsBuilder<AppDbContext>().UseNpgsql(connectionString).Options,
            new FakeCurrentUser(userId));

    /// <summary>
    /// The same database, with the PostgreSQL session on a different clock. Used to
    /// show that a <see cref="DateOnly"/> is not touched by either end's timezone.
    /// </summary>
    public static string InTimeZone(this string connectionString, string timeZone) =>
        new NpgsqlConnectionStringBuilder(connectionString)
        {
            Options = $"-c timezone={timeZone}",
        }.ConnectionString;

    public static Account AnAccount(Guid userId, string name = "Nubank") => new()
    {
        UserId = userId,
        Name = name,
        Type = AccountType.Checking,
        Currency = Account.DefaultCurrency,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>
    /// The default name deliberately avoids all eight seeded categories: every user
    /// now starts with those, and the unique index counts a second top-level "Food"
    /// as the duplicate it is.
    /// </summary>
    public static Category ACategory(
        Guid userId,
        string name = "Groceries",
        CategoryKind kind = CategoryKind.Expense,
        Guid? parentId = null) => new()
    {
        UserId = userId,
        Name = name,
        Kind = kind,
        ParentId = parentId,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    public static Transaction ATransaction(
        Guid userId,
        Guid accountId,
        Guid categoryId,
        decimal amount = -42.90m,
        DateOnly? date = null) => new()
    {
        UserId = userId,
        AccountId = accountId,
        CategoryId = categoryId,
        Money = new Money(amount, Account.DefaultCurrency),
        Date = date ?? new DateOnly(2026, 9, 13),
        Description = "Supermarket",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>
    /// A mutating request carrying the Origin header the CSRF check from 002 demands.
    /// Every write in these tests goes through it, so a missing header shows up as the
    /// 403 it is rather than as a mysterious failure in the endpoint under test.
    /// </summary>
    public static HttpRequestMessage Post(string url, object body) =>
        WithOrigin(new HttpRequestMessage(HttpMethod.Post, url) { Content = JsonContent.Create(body) });

    public static HttpRequestMessage Put(string url, object body) =>
        WithOrigin(new HttpRequestMessage(HttpMethod.Put, url) { Content = JsonContent.Create(body) });

    public static HttpRequestMessage Delete(string url) =>
        WithOrigin(new HttpRequestMessage(HttpMethod.Delete, url));

    /// <summary>
    /// The field names a problem-details response blamed. Spec rule: every 400 names
    /// the offending field, so the assertions check which one rather than only the
    /// status code.
    /// </summary>
    public static async Task<IReadOnlyList<string>> ProblemFieldsAsync(
        HttpResponseMessage response,
        CancellationToken cancellationToken)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync(cancellationToken));

        return document.RootElement.TryGetProperty("errors", out var errors)
            ? [.. errors.EnumerateObject().Select(field => field.Name)]
            : [];
    }

    /// <summary>
    /// The response shapes, declared by the tests rather than shared with the
    /// endpoints: a test that reuses the production DTO cannot notice the endpoint
    /// renaming a field.
    /// </summary>
    public sealed record AccountItem(Guid Id, string Name, string Type, string Currency, DateTimeOffset CreatedAt);

    public sealed record CategoryItem(Guid Id, string Name, string Kind, Guid? ParentId);

    public sealed record TransactionItem(
        Guid Id,
        Guid AccountId,
        string AccountName,
        Guid CategoryId,
        string CategoryName,
        decimal Amount,
        string Currency,
        DateOnly Date,
        string Description,
        DateTimeOffset CreatedAt);

    public sealed record TransactionPage(
        IReadOnlyList<TransactionItem> Items,
        int Page,
        int PageSize,
        int Total);

    /// <summary>The id every create endpoint answers with.</summary>
    public sealed record Created(Guid Id);

    public static Task<Guid> CreateAccountAsync(
        this SignedInUser user,
        string name,
        CancellationToken cancellationToken) =>
        user.CreateAccountAsync(name, "Checking", Account.DefaultCurrency, cancellationToken);

    public static async Task<Guid> CreateAccountAsync(
        this SignedInUser user,
        string name,
        string type,
        string currency,
        CancellationToken cancellationToken)
    {
        using var response = await user.Client.SendAsync(
            Post("/api/accounts", new { name, type, currency }),
            cancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<Created>(cancellationToken))!.Id;
    }

    public static Task<Guid> CreateCategoryAsync(
        this SignedInUser user,
        string name,
        CancellationToken cancellationToken) =>
        user.CreateCategoryAsync(name, nameof(CategoryKind.Expense), parentId: null, cancellationToken);

    public static async Task<Guid> CreateCategoryAsync(
        this SignedInUser user,
        string name,
        string kind,
        Guid? parentId,
        CancellationToken cancellationToken)
    {
        using var response = await user.Client.SendAsync(
            Post("/api/categories", new { name, kind, parentId }),
            cancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<Created>(cancellationToken))!.Id;
    }

    /// <summary>
    /// The body of a transaction request, with every field overridable: most rule
    /// tests are "this request, but one field wrong".
    /// </summary>
    public static object TransactionBody(
        Guid accountId,
        Guid categoryId,
        decimal amount = -42.90m,
        string currency = Account.DefaultCurrency,
        string date = "2026-09-13",
        string description = "Supermarket") =>
        new { accountId, categoryId, amount, currency, date, description };

    public static async Task<Guid> CreateTransactionAsync(
        this SignedInUser user,
        object body,
        CancellationToken cancellationToken)
    {
        using var response = await user.Client.SendAsync(
            Post("/api/transactions", body),
            cancellationToken);

        Assert.Equal(System.Net.HttpStatusCode.Created, response.StatusCode);

        return (await response.Content.ReadFromJsonAsync<Created>(cancellationToken))!.Id;
    }

    private static HttpRequestMessage WithOrigin(HttpRequestMessage request)
    {
        request.Headers.Add("Origin", IdentityApiFactory.AppOrigin);

        return request;
    }

    /// <summary>
    /// Boots the host once so its startup migration runs, which is what puts 003's
    /// tables in the database the contexts below then talk to.
    /// </summary>
    public static async Task MigrateAsync(this IdentityApiFactory factory, CancellationToken cancellationToken)
    {
        using var client = factory.CreateApiClient();
        using var response = await client.GetAsync("/health", cancellationToken);

        response.EnsureSuccessStatusCode();
    }
}
