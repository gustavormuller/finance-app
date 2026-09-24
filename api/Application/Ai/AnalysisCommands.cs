using System.Globalization;
using Finance.Api.Domain.Ai;
using Finance.Api.Infrastructure;
using Finance.Api.Infrastructure.Ai;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Application.Ai;

/// <summary>Why a request for an analysis wrote nothing.</summary>
public enum AnalysisRequestProblem
{
    None,

    /// <summary>The month's analysis is still <c>Pending</c> or <c>Running</c>.</summary>
    InProgress,
}

public sealed record AnalysisRequestResult(Guid AnalysisId, AnalysisRequestProblem Problem);

/// <summary><c>POST /api/ai/analyses</c>: the gates, then the month's <c>Pending</c> row, then the job's wake-up.</summary>
public sealed class AnalysisCommands(
    AppDbContext db, ICurrentUser currentUser, BudgetGuard budget, AnalysisQueue queue, TimeProvider clock)
{
    /// <summary>A <c>YYYY-MM</c> month, as the column holds it.</summary>
    public static bool IsMonth(string? text) =>
        text is { Length: 7 }
        && DateOnly.TryParseExact(text + "-01", "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _);

    /// <summary>
    /// Refuses before any row is written: <c>ai_enabled</c>, then this month's budget (the call
    /// happens now, whatever month it analyses). The job's gateway checks both again.
    /// </summary>
    /// <remarks>
    /// One row per user and month (unique index): a settled row is reset in place and keeps
    /// its id, so regenerating replaces it. A row still in progress is left alone, so no
    /// month is ever paid for twice at once.
    /// </remarks>
    /// <exception cref="AiDisabledException">The user has not turned AI on.</exception>
    /// <exception cref="AiBudgetExceededException">The user's current month has reached the budget.</exception>
    /// <exception cref="DbUpdateException">A concurrent first request for the same month won the unique index.</exception>
    public async Task<AnalysisRequestResult> RequestAsync(string month, CancellationToken ct)
    {
        var userId = currentUser.Id ?? throw new InvalidOperationException("An analysis needs a user.");
        if (!await db.Users.Where(user => user.Id == userId).Select(user => user.AiEnabled).SingleOrDefaultAsync(ct))
        {
            throw new AiDisabledException();
        }

        var now = clock.GetUtcNow();
        await budget.EnsureWithinBudgetAsync(userId, AiCost.MonthOf(now), ct);

        var analysis = await db.AiAnalyses.SingleOrDefaultAsync(row => row.Month == month, ct);
        if (analysis is { Status: AiAnalysisStatus.Pending or AiAnalysisStatus.Running })
        {
            return new AnalysisRequestResult(analysis.Id, AnalysisRequestProblem.InProgress);
        }

        if (analysis is null)
        {
            analysis = new AiAnalysis { Id = Guid.NewGuid(), UserId = userId, Month = month };
            db.AiAnalyses.Add(analysis);
        }

        (analysis.Status, analysis.CreatedAt, analysis.PromptVersion) = (AiAnalysisStatus.Pending, now, MonthlyAnalysisPrompt.Current.Version);
        (analysis.Content, analysis.Error, analysis.StartedAt, analysis.CompletedAt) = (null, null, null, null);
        await db.SaveChangesAsync(ct);

        queue.Enqueue(userId, analysis.Id);
        return new AnalysisRequestResult(analysis.Id, AnalysisRequestProblem.None);
    }
}
