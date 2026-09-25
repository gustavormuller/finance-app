using Finance.Api.Application.MarketData;
using Finance.Api.Domain.MarketData;
using Finance.Api.Infrastructure;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// A test host for 006's endpoints: a database of its own, because the catalogue, the
/// series and the sync runs are shared by every user and the sync rate limit is global,
/// and the providers swapped for the fakes, so a sync never reaches the network.
/// </summary>
internal sealed class MarketDataApi : IAsyncDisposable
{
    private MarketDataApi(string connectionString)
    {
        ConnectionString = connectionString;
        Factory = new IdentityApiFactory(connectionString, services: services =>
        {
            services.AddSingleton<IPriceProviderRegistry>(new PriceProviderRegistry([Brapi, CoinGecko, TwelveData]));
            services.AddSingleton<IBenchmarkProvider>(Bcb);
        });
    }

    public string ConnectionString { get; }

    public IdentityApiFactory Factory { get; }

    public FakePriceProvider Brapi { get; } = new(ProviderKind.Brapi);

    public FakePriceProvider CoinGecko { get; } = new(ProviderKind.CoinGecko);

    public FakePriceProvider TwelveData { get; } = new(ProviderKind.TwelveData);

    public FakeBenchmarkProvider Bcb { get; } = new();

    /// <summary>A migrated database of its own (the host migrates on startup) and a signed-in user.</summary>
    public static async Task<(MarketDataApi Api, HttpClient Client)> StartAsync(
        PostgresFixture postgres, CancellationToken cancellationToken)
    {
        var api = new MarketDataApi(await postgres.CreateEmptyDatabaseAsync(cancellationToken));
        var user = await api.Factory.SignInNewUserAsync("market", cancellationToken);
        return (api, user.Client);
    }

    public AppDbContext Context() => TransactionsFixtures.ContextFor(ConnectionString, null);

    public async ValueTask DisposeAsync()
    {
        await Factory.DisposeAsync();
        PostgresFixture.ReleaseConnections(ConnectionString);
    }

    public sealed record AssetItem(
        Guid Id,
        string Ticker,
        string Name,
        string Class,
        string Currency,
        string Provider,
        string ProviderSymbol,
        bool IsActive,
        DateTimeOffset? LastSyncedAt,
        DateTimeOffset CreatedAt);

    public sealed record PriceItem(DateOnly Date, decimal Close);

    public sealed record BenchmarkItem(DateOnly Date, decimal Value);

    public static object Petr4(string provider = "Brapi", string symbol = "PETR4") => new
    {
        ticker = "PETR4",
        name = "Petrobras PN",
        @class = "StockBr",
        provider,
        providerSymbol = symbol,
        currency = "BRL",
    };
}
