namespace Finance.Api.Domain.Import;

/// <summary>Which parser produced a batch.</summary>
/// <remarks>
/// Stored as <c>int</c>, with the values written down: renumbering them later would
/// silently reinterpret every existing batch.
/// </remarks>
public enum ImportSource
{
    Ofx = 0,
    Csv = 1,

    /// <summary>An <c>.xlsx</c> or <c>.xls</c>, read into the CSV path (spec 011).</summary>
    Spreadsheet = 2,
}
