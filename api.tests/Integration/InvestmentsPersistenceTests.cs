using Finance.Api.Domain;
using Finance.Api.Domain.Investments;
using Finance.Api.Domain.MarketData;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 007 checkpoint 1: the storage layer of investments. The context half of spec
/// integration tests 16, 23 and 24; their HTTP halves arrive with the endpoints.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class InvestmentsPersistenceTests(PostgresFixture postgres)
{
    private const string UniqueViolation = "23505";

    private const string ForeignKeyViolation = "23503";

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

    /// <summary>Spec integration test 23, at the constraint.</summary>
    [Fact]
    public async Task A_user_holds_a_market_asset_once_and_another_user_may_hold_it_too()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (userA, userB) = await TwoUsersAsync(cancellationToken);
        var asset = await HeldAssetAsync(userA, cancellationToken);

        await using (var asUserA = Context(userA))
        {
            asUserA.Set<Asset>().Add(AnAsset(userA, asset.MarketAssetId));

            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => asUserA.SaveChangesAsync(cancellationToken));
            Assert.Equal(UniqueViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
        }

        await using (var asUserB = Context(userB))
        {
            asUserB.Set<Asset>().Add(AnAsset(userB, asset.MarketAssetId));
            await asUserB.SaveChangesAsync(cancellationToken);
        }
    }

    /// <summary>Spec integration test 24, at the constraint: RESTRICT, not CASCADE.</summary>
    [Fact]
    public async Task An_asset_with_movements_cannot_be_deleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (user, _) = await TwoUsersAsync(cancellationToken);
        var asset = await HeldAssetAsync(user, cancellationToken);

        await using (var context = Context(user))
        {
            context.Set<Movement>().Add(ABuy(asset));
            await context.SaveChangesAsync(cancellationToken);
        }

        await using (var context = Context(user))
        {
            context.Set<Asset>().Remove(await context.Set<Asset>().SingleAsync(cancellationToken));

            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(cancellationToken));
            Assert.Equal(ForeignKeyViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
        }
    }

    /// <summary>A held instrument keeps its catalogue row, and with it the price series.</summary>
    [Fact]
    public async Task A_market_asset_someone_holds_cannot_be_deleted()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (user, _) = await TwoUsersAsync(cancellationToken);
        var asset = await HeldAssetAsync(user, cancellationToken);

        await using var context = Context(null);
        context.Set<MarketAsset>().Remove(
            await context.Set<MarketAsset>().SingleAsync(entity => entity.Id == asset.MarketAssetId, cancellationToken));

        var failure = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(cancellationToken));
        Assert.Equal(ForeignKeyViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
    }

    /// <summary>
    /// Daily rows are derived (ADR-011): they never hold their asset in place. An asset
    /// with movements is already refused above, so this is the rows of a rebuild left
    /// behind by a movement that has since been deleted.
    /// </summary>
    [Fact]
    public async Task Deleting_an_asset_deletes_its_daily_rows()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (user, _) = await TwoUsersAsync(cancellationToken);
        var asset = await HeldAssetAsync(user, cancellationToken);

        await using (var context = Context(user))
        {
            context.Set<PortfolioDaily>().AddRange(ADailyRow(asset, Day), ADailyRow(asset, Day.AddDays(1)));
            await context.SaveChangesAsync(cancellationToken);
        }

        await using (var context = Context(user))
        {
            context.Set<Asset>().Remove(await context.Set<Asset>().SingleAsync(cancellationToken));
            await context.SaveChangesAsync(cancellationToken);
        }

        await using (var context = Context(user))
        {
            Assert.Empty(await context.Set<PortfolioDaily>().ToListAsync(cancellationToken));
        }
    }

    /// <summary>
    /// <c>numeric(18,8)</c> for quantities, prices, average cost and FX; <c>numeric(18,2)</c>
    /// for cash and BRL totals. Every value at the edge of its column comes back exact.
    /// </summary>
    [Fact]
    public async Task Quantities_prices_and_cash_round_trip_at_their_column_precision()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var (user, _) = await TwoUsersAsync(cancellationToken);
        var asset = await HeldAssetAsync(user, cancellationToken);

        var movement = ABuy(asset);
        movement.Quantity = 1234567890.12345678m;
        movement.UnitPrice = 0.00012345m;
        movement.Amount = 9999999999999999.99m;
        movement.Fees = 0.01m;
        movement.Notes = "nota de corretagem 123";

        var row = ADailyRow(asset, Day);
        row.Quantity = 0.00000001m;
        row.AverageCost = 0.12345678m;
        row.Price = 9999999999.99999999m;
        row.PriceDate = Day.AddDays(-3);
        row.FxRate = 5.43210987m;
        row.ValueBrl = 9999999999999999.99m;
        row.CostBasisBrl = 0.01m;

        await using (var context = Context(user))
        {
            context.Set<Movement>().Add(movement);
            context.Set<PortfolioDaily>().Add(row);
            await context.SaveChangesAsync(cancellationToken);
        }

        await using (var context = Context(user))
        {
            var storedMovement = await context.Set<Movement>().SingleAsync(cancellationToken);
            Assert.Equal(
                (movement.Quantity, movement.UnitPrice, movement.Amount, movement.Fees, movement.Currency, movement.Notes, movement.Kind, movement.Date),
                (storedMovement.Quantity, storedMovement.UnitPrice, storedMovement.Amount, storedMovement.Fees, storedMovement.Currency, storedMovement.Notes, storedMovement.Kind, storedMovement.Date));

            var storedRow = await context.Set<PortfolioDaily>().SingleAsync(cancellationToken);
            Assert.Equal(
                (row.Quantity, row.AverageCost, row.Price, row.PriceDate, row.FxRate, row.ValueBrl, row.CostBasisBrl),
                (storedRow.Quantity, storedRow.AverageCost, storedRow.Price, storedRow.PriceDate, storedRow.FxRate, storedRow.ValueBrl, storedRow.CostBasisBrl));
        }
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
