namespace Finance.Api.Domain.Transactions;

/// <summary>
/// What kind of account a balance sits in.
/// </summary>
/// <remarks>
/// Stored as <c>int</c>, with the values written down: renumbering them later would
/// silently reinterpret every existing row.
/// </remarks>
public enum AccountType
{
    Checking = 0,
    Savings = 1,
    CreditCard = 2,
    Cash = 3,
    Investment = 4,
}
