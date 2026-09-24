using Cronos;
using Finance.Api.Application.MarketData;
using Microsoft.Extensions.Options;

namespace Finance.Api.Infrastructure.Jobs;

/// <summary>The nightly market-data sync's host (006). Not implemented yet.</summary>
public sealed class MarketDataSyncJob(
    IServiceScopeFactory scopes, IOptions<MarketDataOptions> options, TimeProvider clock, ILogger<MarketDataSyncJob> logger)
    : BackgroundService
{
    internal static DateTimeOffset? NextOccurrence(CronExpression schedule, DateTimeOffset now, TimeZoneInfo zone) =>
        throw new NotImplementedException();

    internal Task RunScheduleAsync(
        CronExpression schedule,
        Func<CancellationToken, Task<DateTimeOffset?>> latestRunStart,
        Func<CancellationToken, Task> run,
        CancellationToken stoppingToken)
    {
        _ = (scopes, options, clock, logger, schedule, latestRunStart, run, stoppingToken);
        throw new NotImplementedException();
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken) => throw new NotImplementedException();
}
