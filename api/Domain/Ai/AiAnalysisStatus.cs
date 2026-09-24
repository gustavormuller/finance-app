namespace Finance.Api.Domain.Ai;

/// <summary>
/// Where an <see cref="AiAnalysis"/> is in its job. The row is the job's state (ADR-003,
/// final form): the channel only wakes the job up. Stored as <c>int</c>, values written down.
/// </summary>
public enum AiAnalysisStatus
{
    /// <summary>Written by the request and enqueued. Re-enqueued at startup after 5 minutes.</summary>
    Pending = 0,

    /// <summary>Picked up by the job.</summary>
    Running = 1,

    /// <summary>Has content.</summary>
    Completed = 2,

    /// <summary>Has an error.</summary>
    Failed = 3,
}
