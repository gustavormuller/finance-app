namespace Finance.Api.Domain.Import;

/// <summary>
/// A saved column mapping for one bank's CSV layout, named by the user. No built-in
/// templates ship: a hardcoded guess at a bank's current layout is an invented fact
/// that goes stale.
/// </summary>
public sealed class CsvTemplate : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>Unique per user.</summary>
    public string Name { get; set; } = "";

    public char Delimiter { get; set; }

    public bool HasHeader { get; set; }

    /// <summary>One of <see cref="AmountParser.SupportedCultures"/>. Governs decimal parsing.</summary>
    public string Culture { get; set; } = "";

    /// <summary>Exact, never inferred.</summary>
    public string DateFormat { get; set; } = "";

    public SignMode SignMode { get; set; }

    /// <summary>A header name, or a 0-based index for files without a header.</summary>
    public string DateColumn { get; set; } = "";

    /// <summary>For <see cref="SignMode.Signed"/> and <see cref="SignMode.SignedInverted"/>.</summary>
    public string? AmountColumn { get; set; }

    /// <summary>For <see cref="SignMode.DebitCredit"/>.</summary>
    public string? DebitColumn { get; set; }

    /// <summary>For <see cref="SignMode.DebitCredit"/>.</summary>
    public string? CreditColumn { get; set; }

    /// <summary>Comma-separated references, joined in order for the description.</summary>
    public string DescriptionColumns { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public CsvMapping ToMapping() => new(
        Delimiter,
        HasHeader,
        Culture,
        DateFormat,
        SignMode,
        DateColumn,
        AmountColumn,
        DebitColumn,
        CreditColumn,
        DescriptionColumns);
}
