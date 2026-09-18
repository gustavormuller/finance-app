namespace Finance.Api.Domain.Import;

/// <summary>Where a batch is in its life: awaiting review, or written to <c>Transactions</c>.</summary>
/// <remarks>
/// Stored as <c>int</c>. <see cref="Staged"/> is 0 because the partial unique index
/// that allows one open batch per user is written as <c>"Status" = 0</c>.
/// </remarks>
public enum ImportBatchStatus
{
    Staged = 0,
    Committed = 1,
}
