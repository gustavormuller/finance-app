using System.Net.Http.Json;
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
