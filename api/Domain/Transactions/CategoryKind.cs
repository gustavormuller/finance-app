namespace Finance.Api.Domain.Transactions;

/// <summary>
/// Which direction of cash flow a category describes.
/// </summary>
/// <remarks>
/// <see cref="Transfer"/> is money moving between the user's own accounts: it
/// changes balances but is neither income nor expense, so aggregations leave it out.
/// <para>
/// Stored as <c>int</c>. The values are written down rather than left implicit,
/// because renumbering them later would silently reinterpret every existing row.
/// </para>
/// </remarks>
public enum CategoryKind
{
    Income = 0,
    Expense = 1,
    Transfer = 2,
}
