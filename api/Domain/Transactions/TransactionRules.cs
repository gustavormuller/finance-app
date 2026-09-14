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

    /// <summary>Spec rule 1. Zero is not a movement of money.</summary>
    public static RuleViolation? ValidateAmount(decimal amount) =>
        amount == 0m
            ? new RuleViolation(AmountField, "Amount may not be zero.")
            : null;

    /// <summary>
    /// Spec rule 3. Keeps "Salary: -3000" and "Rent: +1200" out of the database,
    /// where they would quietly corrupt every later aggregation.
    /// </summary>
    /// <remarks>
    /// Zero passes here and is caught by <see cref="ValidateAmount"/>, so a zero
    /// amount is reported once rather than as two rules disagreeing about it.
    /// </remarks>
    public static RuleViolation? ValidateSign(decimal amount, CategoryKind kind) => kind switch
    {
        CategoryKind.Income when amount < 0m =>
            new RuleViolation(AmountField, "An income category requires a positive amount."),
        CategoryKind.Expense when amount > 0m =>
            new RuleViolation(AmountField, "An expense category requires a negative amount."),
        _ => null,
    };

    /// <summary>Spec rule 5.</summary>
    /// <param name="today">
    /// Passed in rather than read from the clock, so the upper bound is testable and
    /// the function stays pure.
    /// </param>
    public static RuleViolation? ValidateDate(DateOnly date, DateOnly today)
    {
        var latest = today.AddYears(1);

        // Both ends inclusive: a transaction dated exactly 1900-01-01, or exactly a
        // year out, is odd but not a parse error.
        return date < MinimumDate || date > latest
            ? new RuleViolation(
                DateField,
                $"Date must be between {MinimumDate:yyyy-MM-dd} and {latest:yyyy-MM-dd}.")
            : null;
    }
}
