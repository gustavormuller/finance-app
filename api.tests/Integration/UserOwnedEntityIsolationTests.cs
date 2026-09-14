using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// The storage half of spec integration test 1, and the point of the whole exercise:
/// 003 is the first feature with real user-owned entities, so this is the first time
/// the <c>IUserOwned</c> loop from 002 filters something that is not a probe.
/// </summary>
/// <remarks>
/// The HTTP half — that the three list endpoints answer B with empty arrays — arrives
/// with the endpoints. This runs a layer below, where a mistake would be invisible to
/// an endpoint test that only ever saw the filtered result.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class UserOwnedEntityIsolationTests(PostgresFixture postgres)
{
    /// <summary>Spec integration test 1, at the context.</summary>
    [Fact]
    public async Task Accounts_categories_and_transactions_written_by_one_user_are_invisible_to_another()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync(cancellationToken);

        var userA = await factory.SignInNewUserAsync("isolation-a", cancellationToken);
        var userB = await factory.SignInNewUserAsync("isolation-b", cancellationToken);

        await using (var asUserA = TransactionsFixtures.ContextFor(postgres.ConnectionString, userA.Id))
        {
            var account = TransactionsFixtures.AnAccount(userA.Id, "A's account");
            var category = TransactionsFixtures.ACategory(userA.Id, "A's category");
            asUserA.Accounts.Add(account);
            asUserA.Categories.Add(category);
            await asUserA.SaveChangesAsync(cancellationToken);

            asUserA.Transactions.Add(
                TransactionsFixtures.ATransaction(userA.Id, account.Id, category.Id));

            await asUserA.SaveChangesAsync(cancellationToken);
        }

        await using (var asUserB = TransactionsFixtures.ContextFor(postgres.ConnectionString, userB.Id))
        {
            Assert.Empty(await asUserB.Accounts.ToListAsync(cancellationToken));
            Assert.Empty(await asUserB.Transactions.ToListAsync(cancellationToken));

            // B has their own eight seeded categories and must see exactly those —
            // "empty" would be the wrong assertion here, and would also pass if the
            // filter were hiding everything from everyone.
            var categories = await asUserB.Categories.ToListAsync(cancellationToken);
            Assert.All(categories, category => Assert.Equal(userB.Id, category.UserId));
            Assert.DoesNotContain(categories, category => category.Name == "A's category");
        }

        await using (var asUserA = TransactionsFixtures.ContextFor(postgres.ConnectionString, userA.Id))
        {
            // The other half of the filter's contract: it must not hide rows from the
            // person who wrote them.
            Assert.Single(await asUserA.Accounts.ToListAsync(cancellationToken));
            Assert.Single(await asUserA.Transactions.ToListAsync(cancellationToken));
            Assert.Contains(
                await asUserA.Categories.ToListAsync(cancellationToken),
                category => category.Name == "A's category");
        }
    }

    /// <summary>
    /// A row reached by primary key is still reached through the filter. This is the
    /// shape rule 4 depends on: the endpoints resolve an id, and a foreign id has to
    /// come back as nothing at all rather than as a row they then have to remember to
    /// check the owner of.
    /// </summary>
    [Fact]
    public async Task Another_users_row_cannot_be_found_by_its_own_id()
    {
        var cancellationToken = TestContext.Current.CancellationToken;

        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync(cancellationToken);

        var userA = await factory.SignInNewUserAsync("byid-a", cancellationToken);
        var userB = await factory.SignInNewUserAsync("byid-b", cancellationToken);

        Guid accountId;

        await using (var asUserA = TransactionsFixtures.ContextFor(postgres.ConnectionString, userA.Id))
        {
            var account = TransactionsFixtures.AnAccount(userA.Id, "A's only account");
            asUserA.Accounts.Add(account);
            await asUserA.SaveChangesAsync(cancellationToken);
            accountId = account.Id;
        }

        await using (var asUserB = TransactionsFixtures.ContextFor(postgres.ConnectionString, userB.Id))
        {
            Assert.Null(await asUserB.Accounts.SingleOrDefaultAsync(
                account => account.Id == accountId, cancellationToken));

            Assert.False(await asUserB.Accounts.AnyAsync(
                account => account.Id == accountId, cancellationToken));
        }
    }
}
