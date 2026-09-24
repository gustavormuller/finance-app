using System.Net.Http.Json;
using Finance.Api.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// Spec integration test 6. The categories have to exist the moment the account does,
/// or the first thing a new user meets is a form they cannot submit.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class DefaultCategoriesTests(PostgresFixture postgres)
{
    private sealed record Me(Guid Id);

    [Fact]
    public async Task The_default_categories_exist_immediately_after_user_creation()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync(cancellationToken);

        var user = await factory.SignInNewUserAsync("defaults", cancellationToken);

        await using var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id);

        var seeded = await context.Categories.ToListAsync(cancellationToken);

        // Compared as a set of name-and-kind pairs, so the assertion says what the
        // user gets rather than what order the seeder happened to insert in.
        Assert.Equal(
            DefaultCategories.All.OrderBy(category => category.Name, StringComparer.Ordinal),
            seeded.Select(category => (category.Name, category.Kind))
                .OrderBy(category => category.Name, StringComparer.Ordinal));

        Assert.Equal(
            ["Outras receitas", "Salário"],
            seeded.Where(category => category.Kind == CategoryKind.Income)
                .Select(category => category.Name)
                .Order(StringComparer.Ordinal));

        // All top level: the starter set does not guess at how anyone breaks down
        // their spending.
        Assert.All(seeded, category => Assert.Null(category.ParentId));
        Assert.All(seeded, category => Assert.Equal(user.Id, category.UserId));
    }

    /// <summary>
    /// The seeding hangs off account creation, not off signing in. A returning user
    /// takes the find-or-link branch, which must not run it again.
    /// </summary>
    [Fact]
    public async Task Signing_in_again_does_not_seed_a_second_set()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync(cancellationToken);

        var email = $"returning-{Guid.NewGuid():N}@example.com";

        using var first = await factory.CreateSignedInClientAsync(email, null, cancellationToken);
        using var second = await factory.CreateSignedInClientAsync(email, null, cancellationToken);

        var user = await second.GetFromJsonAsync<Me>("/api/auth/me", cancellationToken);

        await using var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user!.Id);

        Assert.Equal(
            DefaultCategories.All.Count,
            await context.Categories.CountAsync(cancellationToken));
    }

    /// <summary>
    /// 005 spec integration test 19. Transfers between the user's own accounts need a
    /// home from day one, or the card-bill payment counts as an expense twice.
    /// </summary>
    [Fact]
    public async Task A_new_user_has_a_top_level_transfer_category()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync(cancellationToken);

        var user = await factory.SignInNewUserAsync("defaults-transfer", cancellationToken);

        await using var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id);

        var transfer = await context.Categories
            .SingleAsync(category => category.Name == "Transferência", cancellationToken);

        Assert.Equal(CategoryKind.Transfer, transfer.Kind);
        Assert.Null(transfer.ParentId);
    }
}
