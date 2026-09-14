namespace Finance.Api.Domain.Transactions;

/// <summary>
/// A rejected field and the reason, ready to become one entry of a problem-details
/// <c>errors</c> object.
/// </summary>
public readonly record struct RuleViolation(string Field, string Message);

/// <summary>
/// The validation rules of 003 that need nothing but their arguments. Pure functions,
/// so they are unit-testable in milliseconds without a database (ADR-014).
/// </summary>
/// <remarks>
/// The rules that are not here — currency matching the account, ids resolving under
/// the current user's filter, a child category being given children — all need a
/// query to decide, and live in <c>Application/</c> next to the context that runs it.
/// </remarks>
public static class TransactionRules
{
    /// <summary>The field name the API reports for an amount rejection.</summary>
    public const string AmountField = "amount";

    /// <summary>The field name the API reports for a date rejection.</summary>
    public const string DateField = "date";

    /// <summary>
    /// Spec rule 5's lower bound. A sanity check against a parse error in 004's
    /// import, not a business rule about how old a transaction may be.
    /// </summary>
    public static readonly DateOnly MinimumDate = new(1900, 1, 1);

    /// <summary>Spec rule 1.</summary>
    public static RuleViolation? ValidateAmount(decimal amount) => throw new NotImplementedException();

    /// <summary>Spec rule 3.</summary>
    public static RuleViolation? ValidateSign(decimal amount, CategoryKind kind) =>
        throw new NotImplementedException();

    /// <summary>Spec rule 5.</summary>
    /// <param name="today">
    /// Passed in rather than read from the clock, so the upper bound is testable and
    /// the function stays pure.
    /// </param>
    public static RuleViolation? ValidateDate(DateOnly date, DateOnly today) =>
        throw new NotImplementedException();
}
