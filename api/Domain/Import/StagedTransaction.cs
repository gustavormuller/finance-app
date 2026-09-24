using Finance.Api.Domain.Transactions;

namespace Finance.Api.Domain.Import;

/// <summary>
/// One row of a batch awaiting review. Nullable where <see cref="Transaction"/> is
/// not, because a row that failed to parse still has to be shown, with its reason,
/// in the preview.
/// </summary>
/// <remarks>
/// Deleted with its batch (<c>CASCADE</c>): staging rows have no life of their own.
/// </remarks>
public sealed class StagedTransaction : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid ImportBatchId { get; set; }

    /// <summary>1-based position in the file, for error messages.</summary>
    public int RowNumber { get; set; }

    /// <summary>Null when unparseable.</summary>
    public DateOnly? Date { get; set; }

    /// <summary>Signed and rounded to two places; null when unparseable.</summary>
    public decimal? Amount { get; set; }

    public string? Currency { get; set; }

    /// <summary>Untruncated, as it came from the file.</summary>
    public string RawDescription { get; set; } = "";

    public string? NormalizedDescription { get; set; }

    /// <summary>The OFX <c>FITID</c>; null for CSV.</summary>
    public string? ExternalId { get; set; }

    /// <summary>The suggestion, or the user's correction. Null when nothing resolved.</summary>
    public Guid? CategoryId { get; set; }

    /// <summary>
    /// Which rung of the cascade chose <see cref="CategoryId"/>, or that the user did.
    /// Not in 009's data model: rung 3 may only touch <see cref="CategorySource.Default"/>
    /// rows, and the preview marks <see cref="CategorySource.Ai"/> ones.
    /// </summary>
    public CategorySource CategorySource { get; set; }

    public StagedRowStatus Status { get; set; }

    /// <summary>
    /// Whether the commit takes this row. Ready rows start included, duplicates
    /// excluded, and the user may flip a duplicate from the preview. Invalid rows are
    /// never included.
    /// </summary>
    /// <remarks>
    /// Not in the spec's column list. The spec's <c>PATCH … { include? }</c> has to
    /// be remembered somewhere, and flipping <see cref="Status"/> would lose the fact
    /// that the row was a duplicate.
    /// </remarks>
    public bool Included { get; set; }

    /// <summary>A JSON array of pt-BR messages; null when there are none.</summary>
    public string? Issues { get; set; }
}
