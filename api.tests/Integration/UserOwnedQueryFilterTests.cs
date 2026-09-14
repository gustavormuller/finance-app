using Finance.Api.Application;
using Finance.Api.Domain;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// A throwaway user-owned entity. It exists only so the generic query filter has
/// something to filter before 003 adds the first real one.
/// </summary>
internal sealed class UserOwnedProbe : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Name { get; set; } = "";
}

/// <summary>
/// Adds one <see cref="IUserOwned"/> entity to the real context and nothing else. It
/// deliberately does not override <c>OnModelCreating</c>: the filter has to come from
/// <see cref="AppDbContext"/> alone, exactly as it will for the entities 003 adds.
/// </summary>
internal sealed class TestDbContext(DbContextOptions<TestDbContext> options, ICurrentUser currentUser)
    : AppDbContext(options, currentUser)
{
    public DbSet<UserOwnedProbe> UserOwnedProbes => Set<UserOwnedProbe>();
}

internal sealed class FakeCurrentUser(Guid? id) : ICurrentUser
{
    public Guid? Id { get; } = id;
}

/// <summary>
/// Spec test 12 — the test the rest of the architecture rests on (ADR-007).
/// </summary>
/// <remarks>
/// The two contexts are separate instances carrying separate
/// <see cref="ICurrentUser"/> values, and EF Core caches one model per context type:
/// the second context runs against the model the first one built. That is the whole
/// point. A filter that captured a value instead of re-reading it per query would
/// show the second user the first user's rows, and would do it silently.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class UserOwnedQueryFilterTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Rows_written_by_one_user_are_invisible_to_another()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await postgres.CreateEmptyDatabaseAsync(cancellationToken);

        var userA = Guid.NewGuid();
        var userB = Guid.NewGuid();

        await using (var asUserA = CreateContext(connectionString, userA))
        {
            await asUserA.Database.EnsureCreatedAsync(cancellationToken);

            asUserA.UserOwnedProbes.Add(new UserOwnedProbe { UserId = userA, Name = "A's row" });
            asUserA.UserOwnedProbes.Add(new UserOwnedProbe { UserId = userA, Name = "A's other row" });
            await asUserA.SaveChangesAsync(cancellationToken);
        }

        // Fresh context, same user: the filter must not hide rows from their owner.
        await using (var asUserA = CreateContext(connectionString, userA))
        {
            Assert.Equal(2, await asUserA.UserOwnedProbes.CountAsync(cancellationToken));
        }

        await using (var asUserB = CreateContext(connectionString, userB))
        {
            Assert.Equal(0, await asUserB.UserOwnedProbes.CountAsync(cancellationToken));
            Assert.Empty(await asUserB.UserOwnedProbes.ToListAsync(cancellationToken));
        }
    }

    /// <summary>
    /// The same query with no authenticated caller. A filter comparing against a null
    /// id has to match nothing; treating "no user" as "no filter" would turn every
    /// unauthenticated code path into a full table read.
    /// </summary>
    [Fact]
    public async Task Rows_are_invisible_when_there_is_no_current_user()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var connectionString = await postgres.CreateEmptyDatabaseAsync(cancellationToken);

        var owner = Guid.NewGuid();

        await using (var asOwner = CreateContext(connectionString, owner))
        {
            await asOwner.Database.EnsureCreatedAsync(cancellationToken);

            asOwner.UserOwnedProbes.Add(new UserOwnedProbe { UserId = owner, Name = "owned" });
            await asOwner.SaveChangesAsync(cancellationToken);
        }

        await using (var anonymous = CreateContext(connectionString, currentUser: null))
        {
            Assert.Equal(0, await anonymous.UserOwnedProbes.CountAsync(cancellationToken));
        }
    }

    private static TestDbContext CreateContext(string connectionString, Guid? currentUser) =>
        new(
            new DbContextOptionsBuilder<TestDbContext>().UseNpgsql(connectionString).Options,
            new FakeCurrentUser(currentUser));
}
