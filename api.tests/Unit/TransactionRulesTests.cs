using Finance.Api.Domain.Transactions;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// Spec unit tests 7-9: the validation rules that decide with nothing but their
/// arguments, so they need no host and no database.
/// </summary>
public sealed class TransactionRulesTests
{
    private static readonly DateOnly Today = new(2026, 9, 14);

    /// <summary>
    /// Spec unit test 7, all four combinations. Also 005's unit test 4: Income and
    /// Expense behave exactly as in 003 now that a third kind exists. This is the rule that stops
    /// "Salary: -3000" and "Rent: +1200" ever reaching the database.
    /// </summary>
    [Theory]
    [InlineData(CategoryKind.Income, 3000, true)]
    [InlineData(CategoryKind.Income, -3000, false)]
    [InlineData(CategoryKind.Expense, -1200, true)]
    [InlineData(CategoryKind.Expense, 1200, false)]
    public void Sign_must_agree_with_the_category_kind(CategoryKind kind, int amount, bool allowed)
    {
        var violation = TransactionRules.ValidateSign(amount, kind);

        if (allowed)
        {
            Assert.Null(violation);

            return;
        }

        Assert.Equal(TransactionRules.AmountField, violation?.Field);
    }

    /// <summary>Spec unit test 8.</summary>
    [Fact]
    public void Zero_is_not_an_amount()
    {
        Assert.Equal(TransactionRules.AmountField, TransactionRules.ValidateAmount(0m)?.Field);

        Assert.Null(TransactionRules.ValidateAmount(0.01m));
        Assert.Null(TransactionRules.ValidateAmount(-0.01m));
    }

    /// <summary>
    /// Spec unit test 9, both ends. The bounds are inclusive, and the first value
    /// outside each one is rejected.
    /// </summary>
    [Fact]
    public void Date_is_bounded_at_both_ends()
    {
        var lastAllowed = Today.AddYears(1);

        Assert.Null(TransactionRules.ValidateDate(TransactionRules.MinimumDate, Today));
        Assert.Null(TransactionRules.ValidateDate(Today, Today));
        Assert.Null(TransactionRules.ValidateDate(lastAllowed, Today));

        Assert.Equal(
            TransactionRules.DateField,
            TransactionRules.ValidateDate(TransactionRules.MinimumDate.AddDays(-1), Today)?.Field);

        Assert.Equal(
            TransactionRules.DateField,
            TransactionRules.ValidateDate(lastAllowed.AddDays(1), Today)?.Field);
    }

    /// <summary>005 spec unit test 1. A transfer arriving in an account is positive.</summary>
    [Fact]
    public void Transfer_accepts_a_positive_amount() =>
        Assert.Null(TransactionRules.ValidateSign(3000m, CategoryKind.Transfer));

    /// <summary>005 spec unit test 2. A transfer leaving an account is negative.</summary>
    [Fact]
    public void Transfer_accepts_a_negative_amount() =>
        Assert.Null(TransactionRules.ValidateSign(-3000m, CategoryKind.Transfer));

    /// <summary>
    /// 005 spec unit test 3. Zero is refused once, by rule 1, and rule 3 stays silent
    /// so the same field is not reported twice (see DEFERRED.md, 005 checkpoint 1).
    /// </summary>
    [Fact]
    public void Transfer_rejects_zero()
    {
        Assert.Equal(TransactionRules.AmountField, TransactionRules.ValidateAmount(0m)?.Field);
        Assert.Null(TransactionRules.ValidateSign(0m, CategoryKind.Transfer));
    }
}
