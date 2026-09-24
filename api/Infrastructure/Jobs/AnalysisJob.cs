using Finance.Api.Application;
using Finance.Api.Application.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Finance.Api.Infrastructure.Jobs;

/// <summary>
/// 009's <c>AnalysisJob</c>, the on-demand kind of ADR-003's final form: a hosted
/// <see cref="BackgroundService"/> reading <see cref="AnalysisQueue"/>, one analysis at a
/// time, each in a scope acting for its user (<see cref="MonthlyAnalysis"/>).
/// </summary>
/// <remarks>
/// <para>
/// The sweep (spec decision 6) runs at startup and then every <see cref="SweepEvery"/>, so a
/// row left <c>Pending</c> by a restart within its first 5 minutes is found too, not only at
/// the next boot. <c>Ai:AnalysisSweep</c> false switches it off; the test hosts do, since
/// they share one database and a sweep would run other tests' rows.
/// </para>
/// <para>
/// A failed run is logged and the job goes on. Stopping mid-call leaves the row
/// <c>Running</c> for the next sweep to fail.
/// </para>
/// </remarks>
public sealed class AnalysisJob(
    AnalysisQueue queue,
    IServiceScopeFactory scopes,
    IOptions<AiOptions> options,
    TimeProvider clock,
    ILogger<AnalysisJob> logger)
    : BackgroundService
{
    public static readonly TimeSpan SweepEvery = TimeSpan.FromMinutes(5);

    protected override Task ExecuteAsync(CancellationToken stoppingToken) =>
        Task.WhenAll(ConsumeAsync(stoppingToken), options.Value.AnalysisSweep ? SweepLoopAsync(stoppingToken) : Task.CompletedTask);

    private async Task ConsumeAsync(CancellationToken stoppingToken)
    {
        await foreach (var item in queue.Reader.ReadAllAsync(stoppingToken))
        {
            try
            {
                await using var scope = scopes.CreateAsyncScope();
                scope.ServiceProvider.GetRequiredService<ActingUser>().ActAs(item.UserId);
                await scope.ServiceProvider.GetRequiredService<MonthlyAnalysis>().RunAsync(item.AnalysisId, stoppingToken);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(error, "AI analysis {AnalysisId} could not be run.", item.AnalysisId);
            }
        }
    }

    private async Task SweepLoopAsync(CancellationToken stoppingToken)
    {
        while (true)
        {
            try
            {
                await SweepOnceAsync(stoppingToken);
            }
            catch (Exception error) when (!stoppingToken.IsCancellationRequested)
            {
                logger.LogError(error, "The AI analysis sweep failed; it runs again in {Minutes} minutes.", SweepEvery.TotalMinutes);
            }

            await Task.Delay(SweepEvery, clock, stoppingToken);
        }
    }

    /// <summary>Each user in a scope of their own, so the filters stay on (no <c>IgnoreQueryFilters</c>).</summary>
    private async Task SweepOnceAsync(CancellationToken stoppingToken)
    {
        List<Guid> users;
        await using (var root = scopes.CreateAsyncScope())
        {
            users = await root.ServiceProvider.GetRequiredService<AppDbContext>().Users.AsNoTracking()
                .OrderBy(user => user.Id).Select(user => user.Id).ToListAsync(stoppingToken);
        }

        foreach (var userId in users)
        {
            await using var scope = scopes.CreateAsyncScope();
            scope.ServiceProvider.GetRequiredService<ActingUser>().ActAs(userId);
            foreach (var analysisId in await scope.ServiceProvider.GetRequiredService<MonthlyAnalysis>().SweepAsync(stoppingToken))
            {
                logger.LogInformation("Re-enqueuing AI analysis {AnalysisId}, left pending.", analysisId);
                queue.Enqueue(userId, analysisId);
            }
        }
    }
}
