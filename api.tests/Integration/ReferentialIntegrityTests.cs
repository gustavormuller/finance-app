using Finance.Api.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// The storage half of spec integration tests 16 and 17: the database itself refuses
/// to orphan a transaction or a child category.
/// </summary>
/// <remarks>
/// The endpoints look for references and answer 409 before attempting the delete,
/// because a readable reason beats a constraint name. These tests exist so
/// that check is a courtesy rather than the only thing between a mistake and lost
/// history — a code path can be forgotten, and RESTRICT cannot.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class ReferentialIntegrityTests(PostgresFixture postgres)
{
    /// <summary>PostgreSQL's SQLSTATE for foreign_key_violation.</summary>
    private const string ForeignKeyViolation = "23503";

    /// <summary>Spec integration test 16, at the constraint.</summary>
    [Fact]
    public async Task An_account_with_transactions_cannot_be_deleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync(cancellationToken);

        var user = await factory.SignInNewUserAsync("delete-account", cancellationToken);
        Guid accountId;

        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            var account = TransactionsFixtures.AnAccount(user.Id);
            var category = TransactionsFixtures.ACategory(user.Id);
            context.Accounts.Add(account);
            context.Categories.Add(category);
            await context.SaveChangesAsync(cancellationToken);

            context.Transactions.Add(
                TransactionsFixtures.ATransaction(user.Id, account.Id, category.Id));

            await context.SaveChangesAsync(cancellationToken);
            accountId = account.Id;
        }

        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            var account = await context.Accounts.SingleAsync(
                entity => entity.Id == accountId, cancellationToken);

            context.Accounts.Remove(account);

            var failure = await Assert.ThrowsAsync<DbUpdateException>(
                () => context.SaveChangesAsync(cancellationToken));

            Assert.Equal(
                ForeignKeyViolation,
                Assert.IsType<PostgresException>(failure.InnerException).SqlState);
        }

        // A refused delete leaves the account and its history exactly as they were.
        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            Assert.True(await context.Accounts.AnyAsync(
                entity => entity.Id == accountId, cancellationToken));

            Assert.Equal(1, await context.Transactions.CountAsync(cancellationToken));
        }
    }

    /// <summary>Spec integration test 17, at the constraint.</summary>
    [Fact]
    public async Task A_category_with_children_cannot_be_deleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync(cancellationToken);

        var user = await factory.SignInNewUserAsync("delete-category", cancellationToken);
        Guid parentId;

        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            var parent = TransactionsFixtures.ACategory(user.Id, "Groceries");
            context.Categories.Add(parent);
            await context.SaveChangesAsync(cancellationToken);

            context.Categories.Add(TransactionsFixtures.ACategory(
                user.Id, "Supermarket", CategoryKind.Expense, parent.Id));

            await context.SaveChangesAsync(cancellationToken);
            parentId = parent.Id;
        }

        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            var parent = await context.Categories.SingleAsync(
                entity => entity.Id == parentId, cancellationToken);

            context.Categories.Remove(parent);

            var failure = await Assert.ThrowsAsync<DbUpdateException>(
                () => context.SaveChangesAsync(cancellationToken));

            Assert.Equal(
                ForeignKeyViolation,
                Assert.IsType<PostgresException>(failure.InnerException).SqlState);
        }

        await using (var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, user.Id))
        {
            Assert.True(await context.Categories.AnyAsync(
                entity => entity.Id == parentId, cancellationToken));
        }
    }
}
