using Finance.Api.Domain.Ai;
using Finance.Api.Infrastructure;
using Finance.Api.Infrastructure.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Finance.Api.Application.Ai;

/// <summary>
/// One run of 009's <c>AnalysisJob</c>, in a scope acting for the analysis's user
/// (<see cref="ActingUser"/>), so the query filters stay on: a scope acting for another user
/// does not find the row and runs nothing.
/// </summary>
public sealed class MonthlyAnalysis(
    AppDbContext db,
    AnalysisInputQueries input,
    AiGateway gateway,
    IOptions<AiOptions> options,
    TimeProvider clock,
    ILogger<MonthlyAnalysis> logger)
{
    /// <summary>
    /// The answer's ceiling. 400 words is about 700 tokens, and the analysis model's thinking
    /// counts against it too; an answer cut at the ceiling fails (CP3).
    /// </summary>
    public const int MaxTokens = 8000;

    /// <summary>The spec's 5 minutes: a row left this long past its due time was lost by a restart.</summary>
    public static readonly TimeSpan StaleAfter = TimeSpan.FromMinutes(5);

    /// <summary>
    /// The spec's steps 2-6. The row is saved <c>Running</c> before the call, because the
    /// gateway saves its usage row on this context. The gateway checks <c>ai_enabled</c> and
    /// the budget again (step 3) and records the usage whatever happens (step 5).
    /// </summary>
    /// <returns>
    /// False when the row is not this user's or no longer <c>Pending</c>, which runs nothing, or
    /// when the user deleted their account while it ran (023), which leaves nothing to record.
    /// </returns>
    public async Task<bool> RunAsync(Guid analysisId, CancellationToken ct)
    {
        var analysis = await db.AiAnalyses.SingleOrDefaultAsync(row => row.Id == analysisId, ct);
        if (analysis is not { Status: AiAnalysisStatus.Pending })
        {
            return false;
        }

        try
        {
            return await RunPendingAsync(analysis, ct);
        }
        catch (DbUpdateException) when (!ct.IsCancellationRequested)
        {
            if (!await UserDeletedAsync(analysis))
            {
                throw;
            }

            return false;
        }
    }

    private async Task<bool> RunPendingAsync(AiAnalysis analysis, CancellationToken ct)
    {
        var prompt = MonthlyAnalysisPrompt.Current;
        (analysis.Status, analysis.StartedAt, analysis.PromptVersion) = (AiAnalysisStatus.Running, clock.GetUtcNow(), prompt.Version);
        (analysis.Content, analysis.Error, analysis.CompletedAt) = (null, null, null);
        await db.SaveChangesAsync(ct);

        try
        {
            var document = AnalysisInputBuilder.Build(await input.AggregatesAsync(analysis.Month, ct));
            var completion = await gateway.CompleteAsync(AiPurpose.Analysis, prompt.System, document, MaxTokens, ct);
            (analysis.Status, analysis.Content) = (AiAnalysisStatus.Completed, completion.Text.Trim());
        }
        catch (Exception failure) when (!ct.IsCancellationRequested)
        {
            if (await UserDeletedAsync(analysis))
            {
                return false;
            }

            // The reason stays in the log (keys redacted by the adapters); the row gets pt-BR copy.
            logger.LogWarning(failure, "AI analysis {AnalysisId} for {Month} failed", analysis.Id, analysis.Month);
            (analysis.Status, analysis.Error) = (AiAnalysisStatus.Failed, AiFailureText.For(failure));
        }

        // Not cancellable, like the usage row: the outcome was paid for.
        analysis.CompletedAt = clock.GetUtcNow();
        await db.SaveChangesAsync(CancellationToken.None);
        return true;
    }

    /// <summary>
    /// 023: an account deleted mid-run took this row with it, and the usage row cannot point at
    /// a user who is gone, so a failed step is not the analysis failing. One quiet line instead.
    /// </summary>
    private async Task<bool> UserDeletedAsync(AiAnalysis analysis)
    {
        if (await db.Users.AnyAsync(user => user.Id == analysis.UserId, CancellationToken.None))
        {
            return false;
        }

        logger.LogInformation("AI analysis {AnalysisId} stopped: its user deleted their account.", analysis.Id);
        return true;
    }

    /// <summary>
    /// The sweep, for this scope's user. A <c>Running</c> row past its timeout and
    /// <see cref="StaleAfter"/> was cut off by a restart: it fails, since its call may already
    /// have been paid for and re-running would pay again unasked. A <c>Pending</c> row older
    /// than <see cref="StaleAfter"/> never reached the provider, so it is returned to be
    /// re-enqueued (spec decision 6).
    /// </summary>
    public async Task<IReadOnlyList<Guid>> SweepAsync(CancellationToken ct)
    {
        var now = clock.GetUtcNow();
        var cutOff = now - StaleAfter - TimeSpan.FromSeconds(options.Value.Analysis.TimeoutSeconds);
        foreach (var interrupted in await db.AiAnalyses
                     .Where(row => row.Status == AiAnalysisStatus.Running && row.StartedAt < cutOff).ToListAsync(ct))
        {
            (interrupted.Status, interrupted.Error, interrupted.CompletedAt) = (AiAnalysisStatus.Failed, AiFailureText.Interrupted, now);
        }

        await db.SaveChangesAsync(ct);
        var stale = now - StaleAfter;
        return await db.AiAnalyses
            .Where(row => row.Status == AiAnalysisStatus.Pending && row.CreatedAt < stale)
            .OrderBy(row => row.CreatedAt)
            .Select(row => row.Id)
            .ToListAsync(ct);
    }
}
