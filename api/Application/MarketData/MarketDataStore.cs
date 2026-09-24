using Finance.Api.Infrastructure;

namespace Finance.Api.Application.MarketData;

/// <summary>Writes provider series into the shared tables. Stub until the AddMarketData migration.</summary>
public sealed class MarketDataStore(AppDbContext db)
{
    private readonly AppDbContext _db = db;

    public Task<int> UpsertPricesAsync(
        Guid marketAssetId, IReadOnlyList<DailyClose> closes, CancellationToken cancellationToken) =>
        throw new NotImplementedException(_db.GetType().Name);

    public Task<int> UpsertBenchmarkAsync(
        string code, IReadOnlyList<DailyValue> values, CancellationToken cancellationToken) =>
        throw new NotImplementedException(_db.GetType().Name);
}
