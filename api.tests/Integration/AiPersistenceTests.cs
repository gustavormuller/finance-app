using Finance.Api.Domain;
using Finance.Api.Domain.Ai;
using Finance.Api.Domain.Import;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// The storage layer of the AI module. The context halves of spec 009 integration tests 13,
/// 14 and 23; the endpoint tests cover their HTTP halves.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class AiPersistenceTests(PostgresFixture postgres)
{
    private const string UniqueViolation = "23505";

    private const string ForeignKeyViolation = "23503";

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

    /// <summary>Spec integration test 23, at the constraint: one analysis per user per month.</summary>
    [Fact]
    public async Task A_user_has_one_analysis_per_month_and_another_user_may_have_the_same_month()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (userA, userB) = await TwoUsersAsync(cancellationToken);

        await using (var asUserA = Context(userA))
        {
            asUserA.Set<AiAnalysis>().AddRange(AnAnalysis(userA, "2026-08"), AnAnalysis(userA, "2026-07"));
            await asUserA.SaveChangesAsync(cancellationToken);
        }

        await using (var asUserA = Context(userA))
        {
            asUserA.Set<AiAnalysis>().Add(AnAnalysis(userA, "2026-08"));

            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => asUserA.SaveChangesAsync(cancellationToken));
            Assert.Equal(UniqueViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
        }

        await using (var asUserB = Context(userB))
        {
            asUserB.Set<AiAnalysis>().Add(AnAnalysis(userB, "2026-08"));
            await asUserB.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>The budget's sum reads this index: every usage row of one user's month.</summary>
    [Fact]
    public void Usage_is_indexed_by_user_and_month()
    {
        using var context = Context(Guid.NewGuid());

        var index = Assert.Single(
            context.Model.FindEntityType(typeof(AiUsage))!.GetIndexes(),
            index => index.Properties.Select(property => property.Name).SequenceEqual([nameof(AiUsage.UserId), nameof(AiUsage.Month)]));
        Assert.False(index.IsUnique);
    }

    /// <summary>Usage belongs to a real user, and goes with them.</summary>
    [Fact]
    public async Task Usage_for_a_user_who_does_not_exist_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await TwoUsersAsync(cancellationToken);
        var stranger = Guid.NewGuid();

        await using var context = Context(stranger);
        context.Set<AiUsage>().Add(AUsage(stranger));

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(cancellationToken));
        Assert.Equal(ForeignKeyViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
    }

    /// <summary>
    /// <c>numeric(10,4)</c> for the cost, at both edges; <c>char(7)</c> months; the
    /// nullable content, error and timestamps; a 500-character error.
    /// </summary>
    [Fact]
    public async Task Usage_and_analyses_round_trip_at_their_column_types()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (user, _) = await TwoUsersAsync(cancellationToken);

        var dearest = AUsage(user);
        dearest.CostBrl = 999999.9999m;
        dearest.InputTokens = int.MaxValue;
        var cheapest = AUsage(user);
        cheapest.CostBrl = 0.0001m;
        cheapest.Purpose = AiPurpose.Categorisation;
        cheapest.Succeeded = false;
        cheapest.OutputTokens = 0;

        var completed = AnAnalysis(user, "2026-08");
        completed.Status = AiAnalysisStatus.Completed;
        completed.Content = "## Resumo\n\nVocê gastou R$ 1.234,56 em agosto. <script>alert(1)</script>";
        completed.StartedAt = completed.CreatedAt.AddSeconds(1);
        completed.CompletedAt = completed.CreatedAt.AddSeconds(20);
        var failed = AnAnalysis(user, "2026-07");
        failed.Status = AiAnalysisStatus.Failed;
        failed.Error = new string('e', 500);

        await using (var context = Context(user))
        {
            context.Set<AiUsage>().AddRange(dearest, cheapest);
            context.Set<AiAnalysis>().AddRange(completed, failed);
            await context.SaveChangesAsync(cancellationToken);
        }

        await using (var context = Context(user))
        {
            var usage = await context.Set<AiUsage>().ToDictionaryAsync(entity => entity.Id, cancellationToken);
            Assert.Equal((999999.9999m, int.MaxValue), (usage[dearest.Id].CostBrl, usage[dearest.Id].InputTokens));
            Assert.Equal(
                (0.0001m, AiPurpose.Categorisation, false, 0, "2026-09", "anthropic", dearest.Model),
                (usage[cheapest.Id].CostBrl, usage[cheapest.Id].Purpose, usage[cheapest.Id].Succeeded,
                    usage[cheapest.Id].OutputTokens, usage[cheapest.Id].Month, usage[cheapest.Id].Provider, usage[cheapest.Id].Model));
            Assert.Equal(1000000.0000m, await context.Set<AiUsage>().SumAsync(entity => entity.CostBrl, cancellationToken));

            var analyses = await context.Set<AiAnalysis>().ToDictionaryAsync(entity => entity.Month, cancellationToken);
            Assert.Equal(
                (AiAnalysisStatus.Completed, completed.Content, (string?)null, "v1", completed.StartedAt, completed.CompletedAt),
                (analyses["2026-08"].Status, analyses["2026-08"].Content, analyses["2026-08"].Error,
                    analyses["2026-08"].PromptVersion, analyses["2026-08"].StartedAt, analyses["2026-08"].CompletedAt));
            Assert.Equal(
                (AiAnalysisStatus.Failed, (string?)null, failed.Error, (DateTimeOffset?)null),
                (analyses["2026-07"].Status, analyses["2026-07"].Content, analyses["2026-07"].Error, analyses["2026-07"].CompletedAt));
        }
    }

    /// <summary>
    /// A staged row remembers which rung chose its category (spec test 18 depends on it),
    /// and a row written without one reads <see cref="CategorySource.None"/>.
    /// </summary>
    [Fact]
    public async Task A_staged_row_keeps_the_source_of_its_category()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (user, _) = await TwoUsersAsync(cancellationToken);
        var (accountId, categoryId) = await ImportFixtures.SeedAccountAndCategoryAsync(postgres.ConnectionString, user, cancellationToken);

        var batch = ImportFixtures.ABatch(user, accountId);
        await using (var context = Context(user))
        {
            context.ImportBatches.Add(batch);
            await context.SaveChangesAsync(cancellationToken);
        }

        var suggested = ImportFixtures.AStagedRow(user, batch.Id, rowNumber: 1);
        suggested.CategoryId = categoryId;
        suggested.CategorySource = CategorySource.Ai;
        var unsourced = ImportFixtures.AStagedRow(user, batch.Id, rowNumber: 2);

        await using (var context = Context(user))
        {
            context.StagedTransactions.AddRange(suggested, unsourced);
            await context.SaveChangesAsync(cancellationToken);
        }

        await using (var context = Context(user))
        {
            var rows = await context.StagedTransactions.OrderBy(row => row.RowNumber).ToListAsync(cancellationToken);
            Assert.Equal(
                [CategorySource.Ai, CategorySource.None],
                rows.Select(row => row.CategorySource));
        }
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
