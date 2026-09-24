using Finance.Api.Domain.MarketData;
using Finance.Api.Infrastructure;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Finance.Api.Application.MarketData;

/// <summary>The nightly market-data sync (006). Not implemented yet.</summary>
public sealed class MarketDataSync(
    AppDbContext db,
    MarketDataStore store,
    IPriceProviderRegistry prices,
    IBenchmarkProvider benchmarks,
    IOptions<MarketDataOptions> options,
    TimeProvider clock,
    ILogger<MarketDataSync> logger)
{
    public const string BenchmarkProviderName = "Bcb";

    public Task<SyncRun> RunAsync(SyncTrigger trigger, CancellationToken cancellationToken)
    {
        _ = (db, store, prices, benchmarks, options, clock, logger, trigger, cancellationToken);
        throw new NotImplementedException();
    }
}
