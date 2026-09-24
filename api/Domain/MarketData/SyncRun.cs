namespace Finance.Api.Domain.MarketData;

/// <summary>
/// One run of the market-data sync: how you find out what happened at 3 a.m.
/// Operational state of a shared job, so not <see cref="IUserOwned"/>.
/// </summary>
/// <remarks>
/// Written as <see cref="SyncRunStatus.Running"/> when the run starts and updated
/// when it ends; a row still <c>Running</c> with no <see cref="FinishedAt"/> long
/// after it started is a run the process did not survive.
/// </remarks>
public sealed class SyncRun
{
    /// <summary>What <see cref="Summary"/> holds before the run has reported anything.</summary>
    public const string EmptySummary = "{}";

    public Guid Id { get; set; }

    public DateTimeOffset StartedAt { get; set; }

    public DateTimeOffset? FinishedAt { get; set; }

    public SyncTrigger Trigger { get; set; }

    public SyncRunStatus Status { get; set; }

    /// <summary>
    /// JSON, stored as <c>jsonb</c>: per provider, the rows written and the error, if
    /// any. Kept as text here so <c>Domain/</c> stays free of a serializer's opinions;
    /// the sync owns its shape.
    /// </summary>
    public string Summary { get; set; } = EmptySummary;
}
