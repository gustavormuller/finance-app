using Finance.Api.Application.MarketData;
using Finance.Api.Domain.Investments;
using Finance.Api.Domain.MarketData;
using Finance.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// A test host for 007's investments: a database of its own, as 006's
/// <see cref="MarketDataApi"/> has, because the catalogue, prices and sync runs are shared
/// by every user and the nightly rebuild walks every user. The providers are the fakes, so
/// nothing reaches the network.
/// </summary>
internal sealed class InvestmentsApi : IAsyncDisposable
{
    private InvestmentsApi(string connectionString, TimeProvider? clock)
    {
        ConnectionString = connectionString;
        Factory = new IdentityApiFactory(connectionString, services: services =>
        {
            services.AddSingleton<IPriceProviderRegistry>(new PriceProviderRegistry([Brapi]));
            services.AddSingleton<IBenchmarkProvider>(Bcb);
            if (clock is not null)
            {
                services.AddSingleton(clock);
            }
        });
    }

    /// <summary>The API's "today": the UTC date, as the sync and the rebuild read it.</summary>
    public static DateOnly Today => DateOnly.FromDateTime(DateTime.UtcNow);

    public string ConnectionString { get; }

    public IdentityApiFactory Factory { get; }

    public FakePriceProvider Brapi { get; } = new(ProviderKind.Brapi);

    public FakeBenchmarkProvider Bcb { get; } = new();

    /// <summary>A database of its own, migrated by booting the host; <paramref name="clock"/> replaces the API's.</summary>
    public static async Task<InvestmentsApi> StartAsync(
        PostgresFixture postgres, CancellationToken cancellationToken, TimeProvider? clock = null)
    {
        var api = new InvestmentsApi(await postgres.CreateEmptyDatabaseAsync(cancellationToken), clock);
        await api.Factory.MigrateAsync(cancellationToken);
        return api;
    }

    public Task<SignedInUser> SignInAsync(string prefix, CancellationToken cancellationToken) =>
        Factory.SignInNewUserAsync(prefix, cancellationToken);

    public AppDbContext Context(Guid? userId) => TransactionsFixtures.ContextFor(ConnectionString, userId);

    /// <summary>A catalogue row, with its closes.</summary>
    public async Task<MarketAsset> CatalogueAsync(
        string ticker, CancellationToken cancellationToken, string currency = "BRL", params (DateOnly Date, decimal Close)[] closes)
    {
        var marketAsset = new MarketAsset
        {
            Id = Guid.NewGuid(),
            Ticker = ticker,
            Name = ticker + " name",
            Class = currency == "BRL" ? MarketAssetClass.StockBr : MarketAssetClass.StockUs,
            Currency = currency,
            Provider = currency == "BRL" ? ProviderKind.Brapi : ProviderKind.TwelveData,
            ProviderSymbol = ticker,
            IsActive = true,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        await using var context = Context(null);
        context.Add(marketAsset);
        context.AddRange(closes.Select(close => new Price { MarketAssetId = marketAsset.Id, Date = close.Date, Close = close.Close }));
        await context.SaveChangesAsync(cancellationToken);
        return marketAsset;
    }

    public async Task UsdBrlAsync(CancellationToken cancellationToken, params (DateOnly Date, decimal Rate)[] rates)
    {
        await using var context = Context(null);
        context.AddRange(rates.Select(rate => new Benchmark { Code = "USDBRL", Date = rate.Date, Value = rate.Rate }));
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>A position and its movements written straight to the database, with no rebuild.</summary>
    public async Task<Asset> HoldAsync(
        Guid userId, MarketAsset marketAsset, CancellationToken cancellationToken, params Movement[] movements)
    {
        var asset = InvestmentsPersistenceTests.AnAsset(userId, marketAsset.Id);
        await using var context = Context(userId);
        context.Add(asset);
        foreach (var movement in movements)
        {
            movement.Id = Guid.NewGuid();
            movement.UserId = userId;
            movement.AssetId = asset.Id;
            movement.Currency = marketAsset.Currency;
            movement.CreatedAt = DateTimeOffset.UtcNow;
            context.Add(movement);
        }

        await context.SaveChangesAsync(cancellationToken);
        return asset;
    }

    public static Movement Buy(DateOnly date, decimal quantity, decimal unitPrice, decimal fees = 0m) =>
        new() { Date = date, Kind = MovementKind.Buy, Quantity = quantity, UnitPrice = unitPrice, Fees = fees };

    public static Movement Sell(DateOnly date, decimal quantity, decimal unitPrice, decimal fees = 0m) =>
        new() { Date = date, Kind = MovementKind.Sell, Quantity = quantity, UnitPrice = unitPrice, Fees = fees };

    public static Movement Dividend(DateOnly date, decimal amount) =>
        new() { Date = date, Kind = MovementKind.Dividend, Amount = amount };

    public ValueTask DisposeAsync() => Factory.DisposeAsync();
}
