namespace Finance.Api.Domain.Transactions;

/// <summary>
/// Which direction of cash flow a category describes.
/// </summary>
/// <remarks>
/// Stored as <c>int</c>. The values are written down rather than left implicit,
/// because renumbering them later would silently reinterpret every existing row.
/// </remarks>
public enum CategoryKind
{
    Income = 0,
    Expense = 1,
    Transfer = 2,
}
