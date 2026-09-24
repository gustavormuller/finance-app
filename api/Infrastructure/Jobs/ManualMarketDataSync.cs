using Finance.Api.Application.MarketData;
using Finance.Api.Domain.MarketData;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Finance.Api.Infrastructure.Jobs;

/// <summary>What <see cref="ManualMarketDataSync.StartAsync"/> did: started a run, or refused and says when to retry.</summary>
public readonly record struct ManualSyncStart(Guid? SyncRunId, TimeSpan RetryAfter);

/// <summary>
/// <c>POST /api/market-data/sync</c> (006, decision 7): at most one run per
/// <see cref="MinimumInterval"/>, globally, counted from the latest
/// <see cref="SyncRun.StartedAt"/> of any trigger in the database, so the limit survives a
/// restart and a nightly run counts too. The <see cref="MarketDataSyncGate"/> is taken
/// before the check and held for the whole run, so two requests at once cannot both pass
/// it and a manual run never overlaps the nightly one.
/// </summary>
/// <remarks>
/// The <c>Running</c> row is written before answering, so the caller gets its id; the run
/// itself happens in the background, in a scope of its own and on the application's
/// stopping token, since the request's scope and token end with the response.
/// </remarks>
public sealed class ManualMarketDataSync(
    IServiceScopeFactory scopes,
    MarketDataSyncGate gate,
    TimeProvider clock,
    IHostApplicationLifetime lifetime,
    IOptions<MarketDataOptions> options,
    ILogger<ManualMarketDataSync> logger)
{
    public static readonly TimeSpan MinimumInterval = TimeSpan.FromMinutes(10);

    /// <summary>
    /// <see cref="MinimumInterval"/>, or zero under <c>MarketData:FakeProviders</c> (E2E,
    /// Development only), where the database is shared and kept between runs. The gate
    /// still keeps runs from overlapping.
    /// </summary>
    private TimeSpan Window => options.Value.FakeProviders ? TimeSpan.Zero : MinimumInterval;

    /// <summary>What a refusal suggests when the latest run started long ago but is still going.</summary>
    private static readonly TimeSpan BusyRetry = TimeSpan.FromMinutes(1);

    public async Task<ManualSyncStart> StartAsync(CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        if (!gate.TryEnter())
        {
            return Refused(await LatestStartAsync(db, cancellationToken));
        }

        Guid syncRunId;
        try
        {
            var latest = await LatestStartAsync(db, cancellationToken);
            if (latest is { } started && clock.GetUtcNow() - started < Window)
            {
                gate.Release();
                return Refused(latest);
            }

            syncRunId = (await scope.ServiceProvider.GetRequiredService<MarketDataSync>()
                .StartAsync(SyncTrigger.Manual, CancellationToken.None)).Id;
        }
        catch
        {
            gate.Release();
            throw;
        }

        // Without the request's execution context: nothing of the request, such as its
        // HttpContext, should be visible to the run.
        using (ExecutionContext.SuppressFlow())
        {
            _ = Task.Run(() => RunAsync(syncRunId), CancellationToken.None);
        }

        return new ManualSyncStart(syncRunId, TimeSpan.Zero);
    }

    private async Task RunAsync(Guid syncRunId)
    {
        try
        {
            await using var scope = scopes.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<MarketDataSync>()
                .ExecuteAsync(syncRunId, lifetime.ApplicationStopping);
        }
        catch (Exception error)
        {
            logger.LogError(error, "The manual market-data sync {SyncRunId} failed.", syncRunId);
        }
        finally
        {
            gate.Release();
        }
    }

    private ManualSyncStart Refused(DateTimeOffset? latest)
    {
        var retry = latest is { } started ? started + Window - clock.GetUtcNow() : TimeSpan.Zero;
        return new ManualSyncStart(null, retry > TimeSpan.Zero ? retry : BusyRetry);
    }

    private static Task<DateTimeOffset?> LatestStartAsync(AppDbContext db, CancellationToken cancellationToken) =>
        db.Set<SyncRun>().MaxAsync(run => (DateTimeOffset?)run.StartedAt, cancellationToken);
}
