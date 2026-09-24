namespace Finance.Api.Infrastructure.Jobs;

/// <summary>007's nightly rebuild after the market-data sync. Not implemented yet.</summary>
public sealed class SnapshotRebuildAfterSync
{
    public const string SummaryKey = "Snapshots";

    public Task RunAsync(Guid syncRunId, CancellationToken cancellationToken) =>
        throw new NotImplementedException();
}
