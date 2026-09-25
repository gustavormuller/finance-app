using Finance.Api.Application.MarketData;
using Finance.Api.Domain;
using Finance.Api.Domain.MarketData;
using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// The storage layer of the shared market-data catalogue. Spec 006 integration tests 17
/// and 18, and the database half of 19.
/// </summary>
/// <remarks>
/// Every table here is shared by all users, and the test database is shared by every
/// test in the collection, so each test invents its own provider symbols and codes.
/// </remarks>
[Collection(nameof(PostgresCollection))]
public sealed class MarketDataPersistenceTests(PostgresFixture postgres)
{
    private const string UniqueViolation = "23505";

    private const string ForeignKeyViolation = "23503";

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

    /// <summary>Spec integration test 18.</summary>
    [Fact]
    public async Task Upserting_a_price_for_a_stored_day_overwrites_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var asset = await StoredAssetAsync(cancellationToken);

        await using (var context = Context(null))
        {
            var store = new MarketDataStore(context);

            Assert.Equal(2, await store.UpsertPricesAsync(
                asset.Id, [new(Day, 10.5m), new(Day.AddDays(1), 11m)], cancellationToken));
            Assert.Equal(2, await store.UpsertPricesAsync(
                asset.Id, [new(Day.AddDays(1), 11.25m), new(Day.AddDays(2), 12m)], cancellationToken));
        }

        Assert.Equal([10.5m, 11.25m, 12m], await ClosesAsync(asset.Id, cancellationToken));
    }

    /// <summary>
    /// A provider can report one day twice (CoinGecko's last point is "now"). One
    /// statement cannot update a row twice, so the batch keeps the last value per day.
    /// </summary>
    [Fact]
    public async Task A_day_repeated_in_one_batch_is_written_once_with_its_last_value()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var asset = await StoredAssetAsync(cancellationToken);

        await using (var context = Context(null))
        {
            Assert.Equal(1, await new MarketDataStore(context).UpsertPricesAsync(
                asset.Id, [new(Day, 1m), new(Day, 2m)], cancellationToken));
            Assert.Equal(0, await new MarketDataStore(context).UpsertPricesAsync(
                asset.Id, [], cancellationToken));
        }

        Assert.Equal([2m], await ClosesAsync(asset.Id, cancellationToken));
    }

    /// <summary><c>numeric(18,8)</c>: eight decimals and ten integer digits survive exactly.</summary>
    [Fact]
    public async Task Closes_round_trip_at_full_precision()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var asset = await StoredAssetAsync(cancellationToken);

        await using (var context = Context(null))
        {
            await new MarketDataStore(context).UpsertPricesAsync(
                asset.Id, [new(Day, 0.00012345m), new(Day.AddDays(1), 1234567890.12345678m)], cancellationToken);
        }

        Assert.Equal([0.00012345m, 1234567890.12345678m], await ClosesAsync(asset.Id, cancellationToken));
    }

    [Fact]
    public async Task A_price_for_an_asset_not_in_the_catalogue_is_refused()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await MigrateAsync(cancellationToken);

        await using var context = Context(null);

        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            new MarketDataStore(context).UpsertPricesAsync(Guid.NewGuid(), [new(Day, 1m)], cancellationToken));
        Assert.Equal(ForeignKeyViolation, failure.SqlState);
    }

    [Fact]
    public async Task Upserting_a_benchmark_value_for_a_stored_day_overwrites_it()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        await MigrateAsync(cancellationToken);
        var code = ACode();

        await using (var context = Context(null))
        {
            var store = new MarketDataStore(context);

            Assert.Equal(1, await store.UpsertBenchmarkAsync(code, [new(Day, 0.05m)], cancellationToken));
            Assert.Equal(2, await store.UpsertBenchmarkAsync(
                code, [new(Day, 0.055131m), new(Day.AddDays(1), 0.055131m)], cancellationToken));
        }

        await using (var context = Context(null))
        {
            var values = await context.Set<Benchmark>()
                .Where(entity => entity.Code == code)
                .OrderBy(entity => entity.Date)
                .Select(entity => entity.Value)
                .ToListAsync(cancellationToken);

            Assert.Equal([0.055131m, 0.055131m], values);
        }
    }

    /// <summary>Spec integration test 19, at the constraint.</summary>
    [Fact]
    public async Task A_provider_symbol_is_unique_per_provider()
    {
        var cancellationToken = TestContext.Current.CancellationToken;
        var first = await StoredAssetAsync(cancellationToken);

        await using (var context = Context(null))
        {
            context.Set<MarketAsset>().Add(AnAsset(first.Provider, first.ProviderSymbol));

            var failure = await Assert.ThrowsAsync<DbUpdateException>(() => context.SaveChangesAsync(cancellationToken));
            Assert.Equal(UniqueViolation, Assert.IsType<PostgresException>(failure.InnerException).SqlState);
        }

        // The same symbol at another provider is another series.
        await using (var context = Context(null))
        {
            context.Set<MarketAsset>().Add(AnAsset(ProviderKind.TwelveData, first.ProviderSymbol));
            await context.SaveChangesAsync(cancellationToken);
        }
    }

    private async Task MigrateAsync(CancellationToken cancellationToken)
    {
        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        await factory.MigrateAsync(cancellationToken);
    }

    private async Task<MarketAsset> StoredAssetAsync(CancellationToken cancellationToken)
    {
        await MigrateAsync(cancellationToken);

        var asset = AnAsset();
        await using var context = Context(null);
        context.Set<MarketAsset>().Add(asset);
        await context.SaveChangesAsync(cancellationToken);

        return asset;
    }

    private async Task<List<decimal>> ClosesAsync(Guid assetId, CancellationToken cancellationToken)
    {
        await using var context = Context(null);

        return await context.Set<Price>()
            .Where(entity => entity.MarketAssetId == assetId)
            .OrderBy(entity => entity.Date)
            .Select(entity => entity.Close)
            .ToListAsync(cancellationToken);
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
