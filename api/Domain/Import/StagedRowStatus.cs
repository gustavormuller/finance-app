namespace Finance.Api.Domain.Import;

/// <summary>What the preview says about a staged row.</summary>
/// <remarks>Stored as <c>int</c>, with the values written down.</remarks>
public enum StagedRowStatus
{
    /// <summary>Parsed, valid, not seen before. Included in the commit by default.</summary>
    Ready = 0,

    /// <summary>Matches a committed transaction or an earlier row of the batch. Excluded by default; the user may include it.</summary>
    Duplicate = 1,

    /// <summary>Carries at least one issue. Can never be included.</summary>
    Invalid = 2,
}
