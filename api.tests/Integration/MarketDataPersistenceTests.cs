using Finance.Api.Domain;
using Finance.Api.Domain.MarketData;
using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 006 checkpoint 1: the storage layer of the shared market-data catalogue. Spec
/// integration tests 17 and 18, and the database half of 19.
/// </summary>
/// <remarks>
/// Every table here is shared by all users, and the test database is shared by every
/// test in the collection, so each test invents its own provider symbols and codes.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class MarketDataPersistenceTests(PostgresFixture postgres)
{
    private static readonly DateOnly Day = new(2026, 9, 1);

    public static TheoryData<Type> SharedTypes =>
        [typeof(MarketAsset), typeof(Price), typeof(Benchmark), typeof(SyncRun)];

    /// <summary>
    /// Spec integration test 17: the inverse of every isolation test so far, and
    /// deliberate. Written as user A, read as user B and as nobody.
    /// </summary>
    [Fact]
    public async Task Market_data_written_as_one_user_is_read_by_another_and_by_no_user()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await MigrateAsync(cancellationToken);

        var asset = AnAsset();
        var code = ACode();
        var run = new SyncRun
        {
            Id = Guid.NewGuid(),
            StartedAt = DateTimeOffset.UtcNow,
            Trigger = SyncTrigger.Manual,
            Status = SyncRunStatus.Running,
        };

        await using (var asUserA = Context(Guid.NewGuid()))
        {
            asUserA.Set<MarketAsset>().Add(asset);
            asUserA.Set<Price>().Add(new Price { MarketAssetId = asset.Id, Date = Day, Close = 38.12m });
            asUserA.Set<Benchmark>().Add(new Benchmark { Code = code, Date = Day, Value = 0.055131m });
            asUserA.Set<SyncRun>().Add(run);
            await asUserA.SaveChangesAsync(cancellationToken);
        }

        foreach (var reader in new Guid?[] { Guid.NewGuid(), null })
        {
            await using var context = Context(reader);

            Assert.True(await context.Set<MarketAsset>().AnyAsync(entity => entity.Id == asset.Id, cancellationToken));
            Assert.True(await context.Set<Price>().AnyAsync(entity => entity.MarketAssetId == asset.Id, cancellationToken));
            Assert.True(await context.Set<Benchmark>().AnyAsync(entity => entity.Code == code, cancellationToken));
            Assert.True(await context.Set<SyncRun>().AnyAsync(entity => entity.Id == run.Id, cancellationToken));
        }
    }

    /// <summary>
    /// Test 17 at the model: no <see cref="IUserOwned"/>, no <c>UserId</c>, no filter.
    /// <see cref="Account"/> is the control that proves the filter lookup sees filters.
    /// </summary>
    [Theory]
    [MemberData(nameof(SharedTypes))]
    public void Market_data_types_are_not_user_owned_and_carry_no_query_filter(Type type)
    {
        Assert.False(typeof(IUserOwned).IsAssignableFrom(type));
        Assert.Null(type.GetProperty("UserId"));

        using var context = Context(Guid.NewGuid());

        Assert.NotEmpty(context.Model.FindEntityType(typeof(Account))!.GetDeclaredQueryFilters());

        var entityType = context.Model.FindEntityType(type);
        Assert.NotNull(entityType);
        Assert.Empty(entityType.GetDeclaredQueryFilters());
    }

    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync(cancellationToken);
    }

    private AppDbContext Context(Guid? userId) => TransactionsFixtures.ContextFor(postgres.ConnectionString, userId);

    private static MarketAsset AnAsset(ProviderKind provider = ProviderKind.Brapi, string? symbol = null) => new()
    {
        Id = Guid.NewGuid(),
        Ticker = "PETR4",
        Name = "Petrobras PN",
        Class = MarketAssetClass.StockBr,
        Currency = "BRL",
        Provider = provider,
        ProviderSymbol = symbol ?? "S" + Guid.NewGuid().ToString("N")[..20],
        IsActive = true,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    private static string ACode() => "C" + Guid.NewGuid().ToString("N")[..12];
}
