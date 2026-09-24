using Finance.Api.Domain;
using Finance.Api.Domain.Investments;
using Finance.Api.Domain.MarketData;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 007 checkpoint 1: the storage layer of investments. The context half of spec
/// integration tests 16, 23 and 24; their HTTP halves arrive with the endpoints.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class InvestmentsPersistenceTests(PostgresFixture postgres)
{
    private static readonly DateOnly Day = new(2026, 9, 1);

    public static TheoryData<Type> UserOwnedTypes => [typeof(Asset), typeof(Movement), typeof(PortfolioDaily)];

    /// <summary>Spec integration test 16, at the context: by list and by id.</summary>
    [Fact]
    public async Task Assets_movements_and_daily_rows_written_by_one_user_are_invisible_to_another()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (userA, userB) = await TwoUsersAsync(cancellationToken);
        var asset = await HeldAssetAsync(userA, cancellationToken);

        await using (var asUserA = Context(userA))
        {
            asUserA.Set<Movement>().Add(ABuy(asset));
            asUserA.Set<PortfolioDaily>().Add(ADailyRow(asset, Day));
            await asUserA.SaveChangesAsync(cancellationToken);
        }

        await using (var asUserB = Context(userB))
        {
            Assert.Empty(await asUserB.Set<Asset>().ToListAsync(cancellationToken));
            Assert.Empty(await asUserB.Set<Movement>().ToListAsync(cancellationToken));
            Assert.Empty(await asUserB.Set<PortfolioDaily>().ToListAsync(cancellationToken));
            Assert.Null(await asUserB.Set<Asset>().SingleOrDefaultAsync(entity => entity.Id == asset.Id, cancellationToken));
            Assert.False(await asUserB.Set<Movement>().AnyAsync(entity => entity.AssetId == asset.Id, cancellationToken));
        }

        await using (var asUserA = Context(userA))
        {
            Assert.Single(await asUserA.Set<Asset>().ToListAsync(cancellationToken));
            Assert.Single(await asUserA.Set<Movement>().ToListAsync(cancellationToken));
            Assert.Single(await asUserA.Set<PortfolioDaily>().ToListAsync(cancellationToken));
        }
    }

    /// <summary>
    /// Test 16 at the model. The market-data types are the control that proves a
    /// missing filter would be seen (<see cref="MarketDataPersistenceTests"/>).
    /// </summary>
    [Theory]
    [MemberData(nameof(UserOwnedTypes))]
    public void Investment_types_are_user_owned_and_carry_a_query_filter(Type type)
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

        var userA = await factory.SignInNewUserAsync("investor-a", cancellationToken);
        var userB = await factory.SignInNewUserAsync("investor-b", cancellationToken);

        return (userA.Id, userB.Id);
    }

    /// <summary>A fresh catalogue row, and a position in it held by <paramref name="userId"/>.</summary>
    private async Task<Asset> HeldAssetAsync(Guid userId, CancellationToken cancellationToken)
    {
        var marketAsset = new MarketAsset
        {
            Id = Guid.NewGuid(),
            Ticker = "PETR4",
            Name = "Petrobras PN",
            Class = MarketAssetClass.StockBr,
            Currency = "BRL",
            Provider = ProviderKind.Brapi,
            ProviderSymbol = "S" + Guid.NewGuid().ToString("N")[..20],
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var asset = AnAsset(userId, marketAsset.Id);

        await using var context = Context(userId);
        context.Set<MarketAsset>().Add(marketAsset);
        context.Set<Asset>().Add(asset);
        await context.SaveChangesAsync(cancellationToken);

        return asset;
    }

    private AppDbContext Context(Guid? userId) => TransactionsFixtures.ContextFor(postgres.ConnectionString, userId);

    internal static Asset AnAsset(Guid userId, Guid marketAssetId) => new()
    {
        Id = Guid.NewGuid(),
        UserId = userId,
        MarketAssetId = marketAssetId,
        CreatedAt = DateTimeOffset.UtcNow,
    };

    internal static Movement ABuy(Asset asset) => new()
    {
        Id = Guid.NewGuid(),
        UserId = asset.UserId,
        AssetId = asset.Id,
        Date = Day,
        Kind = MovementKind.Buy,
        Quantity = 100m,
        UnitPrice = 10m,
        Fees = 5m,
        Currency = "BRL",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    internal static PortfolioDaily ADailyRow(Asset asset, DateOnly date) => new()
    {
        UserId = asset.UserId,
        AssetId = asset.Id,
        Date = date,
        Quantity = 100m,
        AverageCost = 10.05m,
        Price = 11m,
        PriceDate = date,
        FxRate = 1m,
        ValueBrl = 1100m,
        CostBasisBrl = 1005m,
    };
}
