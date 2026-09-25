using Finance.Api.Application;
using Finance.Api.Application.Investments;
using Finance.Api.Application.MarketData;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Infrastructure.Jobs;

/// <summary>
/// 007's nightly rebuild: after a market-data sync, every user's assets are rebuilt from
/// yesterday (earlier where rows are missing, <see cref="SnapshotRebuild.NightlyFromAsync"/>),
/// and the run's <see cref="Domain.MarketData.SyncRun.Summary"/> gains a
/// <see cref="SummaryKey"/> section. Runs under <see cref="MarketDataSyncGate"/>, after the
/// sync, in both the scheduled job and the manual trigger.
/// </summary>
/// <remarks>
/// <para>
/// There is no signed-in user, so each user gets a scope of their own with
/// <see cref="ActingUser"/> set: the query filters stay on, and no query here reaches
/// another user's rows.
/// </para>
/// <para>
/// <b>The section holds counts only.</b> Every signed-in user sees the summary on
/// <c>/market-data</c>, so it names no ticker, asset or user; a failure is logged with its
/// ids and counted in the summary. Each asset is rebuilt in its own transaction, so one
/// failing asset does not stop the others.
/// </para>
/// </remarks>
public sealed class SnapshotRebuildAfterSync(
    IServiceScopeFactory scopes,
    TimeProvider clock,
    ILogger<SnapshotRebuildAfterSync> logger)
{
    public const string SummaryKey = "Snapshots";

    public const string FailureText = "Não foi possível recalcular as posições de um ou mais ativos.";

    public async Task RunAsync(Guid syncRunId, CancellationToken cancellationToken)
    {
        var yesterday = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime).AddDays(-1);
        await using var root = scopes.CreateAsyncScope();
        var db = root.ServiceProvider.GetRequiredService<AppDbContext>();
        var users = await db.Users.AsNoTracking().OrderBy(user => user.Id).Select(user => user.Id).ToListAsync(cancellationToken);

        var part = new ProviderSyncSummary();
        foreach (var userId in users)
        {
            await RebuildUserAsync(userId, yesterday, part, cancellationToken);
        }

        var run = await db.SyncRuns.SingleAsync(entity => entity.Id == syncRunId, cancellationToken);
        var summary = SyncSummaryJson.Read(run.Summary);
        if (part.ItemsSynced + part.ItemsFailed > 0)
        {
            summary[SummaryKey] = part;
            run.Summary = SyncSummaryJson.Write(summary);
            run.Status = MarketDataSync.StatusOf(summary.Values);
            await db.SaveChangesAsync(cancellationToken);
        }

        logger.LogInformation(
            "Snapshot rebuild after sync {SyncRunId}: {Rebuilt} assets rebuilt, {Failed} failed.",
            syncRunId, part.ItemsSynced, part.ItemsFailed);
    }

    private async Task RebuildUserAsync(Guid userId, DateOnly yesterday, ProviderSyncSummary part, CancellationToken cancellationToken)
    {
        await using var scope = scopes.CreateAsyncScope();
        scope.ServiceProvider.GetRequiredService<ActingUser>().ActAs(userId);
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var rebuild = scope.ServiceProvider.GetRequiredService<SnapshotRebuild>();

        foreach (var assetId in await db.Assets.Select(asset => asset.Id).ToListAsync(cancellationToken))
        {
            try
            {
                var from = await rebuild.NightlyFromAsync(assetId, yesterday, cancellationToken);
                part.RowsWritten += await rebuild.RebuildAsync(assetId, from, cancellationToken);
                part.ItemsSynced++;
            }
            catch (Exception error) when (!cancellationToken.IsCancellationRequested)
            {
                // 023: the account was deleted after this run listed it. Its assets went with
                // it, so nothing failed and the summary every user sees must not say so.
                if (!await db.Users.AnyAsync(user => user.Id == userId, cancellationToken))
                {
                    logger.LogInformation("Snapshot rebuild for user {UserId} stopped: the account was deleted.", userId);
                    return;
                }

                logger.LogError(error, "Snapshot rebuild of asset {AssetId} for user {UserId} failed.", assetId, userId);
                part.ItemsFailed++;
                part.Error = FailureText;
            }
        }
    }
}
