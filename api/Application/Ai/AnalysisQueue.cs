namespace Finance.Api.Application.Ai;

/// <summary>One analysis to run, and the user whose scope runs it.</summary>
public sealed record AnalysisWorkItem(Guid UserId, Guid AnalysisId);

/// <summary>The analysis job's wake-up channel (ADR-003, final form).</summary>
public sealed class AnalysisQueue
{
    public void Enqueue(Guid userId, Guid analysisId) => throw new NotImplementedException();
}
