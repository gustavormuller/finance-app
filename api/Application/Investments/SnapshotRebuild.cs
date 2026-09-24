using Finance.Api.Infrastructure;

namespace Finance.Api.Application.Investments;

/// <summary>The spec's <c>RebuildSnapshots</c>. Not implemented yet.</summary>
public sealed class SnapshotRebuild(AppDbContext db, TimeProvider clock)
{
    public Task<int> RebuildAsync(Guid assetId, DateOnly from, CancellationToken cancellationToken) =>
        throw new NotImplementedException($"{db}{clock}");
}
