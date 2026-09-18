using Finance.Api.Domain.Transactions;

namespace Finance.Api.Domain.Import;

/// <summary>
/// One uploaded file, from staging to commit. It is what makes an import
/// reviewable before it touches <see cref="Transaction"/> and undoable after
/// (ARCHITECTURE.md §2).
/// </summary>
/// <remarks>
/// One open batch per user at a time, enforced by a partial unique index on
/// <c>(UserId) where Status = Staged</c> rather than by a pre-check that races.
/// </remarks>
public sealed class ImportBatch : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The account every row of the file goes into, and whose currency they must match.</summary>
    public Guid AccountId { get; set; }

    public ImportSource Source { get; set; }

    public string FileName { get; set; } = "";

    public ImportBatchStatus Status { get; set; }

    /// <summary>Rows parsed from the file, whatever their status.</summary>
    public int RowCount { get; set; }

    /// <summary>Rows actually written to <see cref="Transaction"/>. Null until committed.</summary>
    public int? CommittedCount { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? CommittedAt { get; set; }
}
