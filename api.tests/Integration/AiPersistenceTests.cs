using Finance.Api.Domain;
using Finance.Api.Domain.Ai;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 009 checkpoint 1: the storage layer of the AI module. The context halves of spec
/// integration tests 13, 14 and 23; their HTTP halves arrive with the endpoints.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class AiPersistenceTests(PostgresFixture postgres)
{
    public static TheoryData<Type> UserOwnedTypes => [typeof(AiUsage), typeof(AiAnalysis)];

    /// <summary>Spec integration tests 13 and 14, at the context: by list and by id.</summary>
    [Fact]
    public async Task A_users_usage_and_analyses_are_invisible_to_another_user()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (userA, userB) = await TwoUsersAsync(cancellationToken);
        var usage = AUsage(userA);
        var analysis = AnAnalysis(userA, "2026-08");

        await using (var asUserA = Context(userA))
        {
            asUserA.Set<AiUsage>().Add(usage);
            asUserA.Set<AiAnalysis>().Add(analysis);
            await asUserA.SaveChangesAsync(cancellationToken);
        }

        await using (var asUserB = Context(userB))
        {
            Assert.Empty(await asUserB.Set<AiUsage>().ToListAsync(cancellationToken));
            Assert.Empty(await asUserB.Set<AiAnalysis>().ToListAsync(cancellationToken));
            Assert.Null(await asUserB.Set<AiAnalysis>().SingleOrDefaultAsync(entity => entity.Id == analysis.Id, cancellationToken));
            Assert.Equal(0m, await asUserB.Set<AiUsage>().Where(entity => entity.Month == usage.Month).SumAsync(entity => entity.CostBrl, cancellationToken));
        }

        await using (var asUserA = Context(userA))
        {
            Assert.Single(await asUserA.Set<AiUsage>().ToListAsync(cancellationToken));
            Assert.NotNull(await asUserA.Set<AiAnalysis>().SingleOrDefaultAsync(entity => entity.Id == analysis.Id, cancellationToken));
        }
    }

    /// <summary>Tests 13 and 14 at the model: both types are user-owned and filtered.</summary>
    [Theory]
    [MemberData(nameof(UserOwnedTypes))]
    public void Ai_types_are_user_owned_and_carry_a_query_filter(Type type)
    {
        Assert.True(typeof(IUserOwned).IsAssignableFrom(type));

        using var context = Context(Guid.NewGuid());

        var entityType = context.Model.FindEntityType(type);
        Assert.NotNull(entityType);
        Assert.NotEmpty(entityType.GetDeclaredQueryFilters());
    }

    private async Task<(Guid, Guid)> TwoUsersAsync(CancellationToken cancellationToken)
    {
        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync(cancellationToken);

        var userA = await factory.SignInNewUserAsync("ai-a", cancellationToken);
        var userB = await factory.SignInNewUserAsync("ai-b", cancellationToken);

        return (userA.Id, userB.Id);
    }

    private AppDbContext Context(Guid? userId) => TransactionsFixtures.ContextFor(postgres.ConnectionString, userId);

    internal static AiUsage AUsage(Guid userId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Month = "2026-09",
        Purpose = AiPurpose.Analysis,
        Provider = "anthropic",
        Model = "model-" + new string('m', 94),
        InputTokens = 30000,
        OutputTokens = 800,
        CostBrl = 0.1234m,
        Succeeded = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    internal static AiAnalysis AnAnalysis(Guid userId, string month) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        Month = month,
        Status = AiAnalysisStatus.Pending,
        PromptVersion = "v1",
        CreatedAt = TruncatedNow(),
    };

    /// <summary>PostgreSQL keeps microseconds; .NET ticks are a tenth of one.</summary>
    private static DateTimeOffset TruncatedNow()
    {
        var now = DateTimeOffset.UtcNow;
        return now.AddTicks(-(now.Ticks % 10));
    }
}
