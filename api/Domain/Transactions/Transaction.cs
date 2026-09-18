namespace Finance.Api.Domain.Transactions;

/// <summary>
/// One dated movement of money. A fact, above the line: never overwritten by a
/// derivation, and the thing every balance in the system is computed from
/// (ARCHITECTURE.md, principle 3).
/// </summary>
public sealed class Transaction : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid AccountId { get; set; }

    public Guid CategoryId { get; set; }

    /// <summary>
    /// Signed: negative is money leaving, positive is money arriving. The sign is the
    /// source of truth for direction — there is no separate kind column to disagree
    /// with it.
    /// </summary>
    /// <remarks>
    /// Mapped as an EF complex property to two columns, <c>Amount numeric(18,2)</c>
    /// and <c>Currency char(3)</c>. A complex property rather than <c>OwnsOne</c>,
    /// because <see cref="Money"/> has no identity of its own and an owned entity
    /// would invent one.
    /// </remarks>
    public Money Money { get; set; }

    /// <summary>
    /// The calendar day, not an instant. Bank statements give days, and a
    /// <c>timestamptz</c> here would invite timezone bugs for no benefit.
    /// </summary>
    public DateOnly Date { get; set; }

    public string Description { get; set; } = "";

    /// <summary>When the row was written. This one <em>is</em> an instant.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// The batch that imported this row, so a whole import can be undone. Null for
    /// a row entered by hand.
    /// </summary>
    public Guid? ImportBatchId { get; set; }

    /// <summary>
    /// The institution's own id for the movement — the OFX <c>FITID</c>. Unique per
    /// account where present, which is what makes a re-import of the same file a
    /// no-op. Null for manual rows and CSV imports.
    /// </summary>
    public string? ExternalId { get; set; }

    /// <summary>
    /// The dedupe and history key (ADR-012 rung 2). Null for rows created before
    /// 004, which are never matched — they were entered by hand. No backfill.
    /// </summary>
    public string? NormalizedDescription { get; set; }
}
