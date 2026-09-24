using Cronos;
using Finance.Api.Application.MarketData;
using Finance.Api.Domain.MarketData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Finance.Api.Infrastructure.Jobs;

/// <summary>
/// Hosts the nightly market-data sync (006; ADR-003 as amended). Cronos computes the next
/// occurrence of <c>MarketData:Schedule</c> in the server's local time zone, the job waits
/// until then, runs <see cref="MarketDataSync"/> in a scope of its own, and loops. At
/// startup, if the latest <see cref="SyncRun"/> started more than 26 hours ago, or there is
/// none, it runs at once: the box was down at 3 a.m.
/// </summary>
/// <remarks>
/// <c>MarketData:ScheduledSync</c> false switches it off; the test hosts and the E2E run
/// do, so no test syncs against the network. A cron expression that does not parse fails
/// the boot. A failed run is logged and the schedule goes on.
/// </remarks>
public sealed class MarketDataSyncJob(
    IServiceScopeFactory scopes,
    IOptions<MarketDataOptions> options,
    TimeProvider clock,
    ILogger<MarketDataSyncJob> logger,
    MarketDataSyncGate gate)
    : BackgroundService
{
    /// <summary>A latest run older than this at startup means the scheduled one was missed.</summary>
    public static readonly TimeSpan Overdue = TimeSpan.FromHours(26);

    private CronExpression? schedule;

    internal static DateTimeOffset? NextOccurrence(CronExpression schedule, DateTimeOffset now, TimeZoneInfo zone) =>
        schedule.GetNextOccurrence(now, zone);

    public override Task StartAsync(CancellationToken cancellationToken)
    {
        if (options.Value.ScheduledSync)
        {
            schedule = CronExpression.TryParse(options.Value.Schedule, out var parsed)
                ? parsed
                : throw new InvalidOperationException(
                    $"MarketData:Schedule \"{options.Value.Schedule}\" is not a five-field cron expression.");
        }

        return base.StartAsync(cancellationToken);
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (schedule is null)
        {
            logger.LogInformation("Scheduled market-data sync is off (MarketData:ScheduledSync).");
            return Task.CompletedTask;
        }

        return RunScheduleAsync(schedule, LatestRunStartAsync, RunOnceAsync, stoppingToken);
    }

    /// <summary>The loop, with the database behind two delegates so it can be driven on a fake clock.</summary>
    internal async Task RunScheduleAsync(
        CronExpression schedule,
        Func<CancellationToken, Task<DateTimeOffset?>> latestRunStart,
        Func<CancellationToken, Task> run,
        CancellationToken stoppingToken)
    {
        if (await IsOverdueAsync(latestRunStart, stoppingToken))
        {
            await RunSafelyAsync(run, stoppingToken);
        }

        while (true)
        {
            var next = NextOccurrence(schedule, clock.GetUtcNow(), clock.LocalTimeZone);
            if (next is null)
            {
                logger.LogWarning("MarketData:Schedule has no next occurrence; the market-data job stops.");
                return;
            }

            // In steps of at most a day: Task.Delay refuses anything past about 49 days.
            for (var remaining = next.Value - clock.GetUtcNow(); remaining > TimeSpan.Zero; remaining = next.Value - clock.GetUtcNow())
            {
                await Task.Delay(remaining < TimeSpan.FromDays(1) ? remaining : TimeSpan.FromDays(1), clock, stoppingToken);
            }

            await RunSafelyAsync(run, stoppingToken);
        }
    }

    private async Task<bool> IsOverdueAsync(
        Func<CancellationToken, Task<DateTimeOffset?>> latestRunStart, CancellationToken stoppingToken)
    {
        try
        {
            var latest = await latestRunStart(stoppingToken);
            return latest is null || clock.GetUtcNow() - latest.Value > Overdue;
        }
        catch (Exception error) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(error, "Could not read the latest market-data sync run; waiting for the schedule.");
            return false;
        }
    }

    private async Task RunSafelyAsync(Func<CancellationToken, Task> run, CancellationToken stoppingToken)
    {
        try
        {
            await run(stoppingToken);
        }
        catch (Exception error) when (!stoppingToken.IsCancellationRequested)
        {
            logger.LogError(error, "The scheduled market-data sync failed.");
        }
    }

    private async Task<DateTimeOffset?> LatestRunStartAsync(CancellationToken stoppingToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
            .Set<SyncRun>().MaxAsync(run => (DateTimeOffset?)run.StartedAt, stoppingToken);
    }

    /// <summary>Behind the gate the manual trigger uses: waits out a manual run in progress, then runs.</summary>
    private async Task RunOnceAsync(CancellationToken stoppingToken)
    {
        await gate.WaitAsync(stoppingToken);
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<MarketDataSync>().RunAsync(SyncTrigger.Scheduled, stoppingToken);
        }
        finally
        {
            gate.Release();
        }
    }
}
