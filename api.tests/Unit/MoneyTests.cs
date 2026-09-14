using System.Globalization;
using Finance.Api.Domain.Transactions;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// Spec unit tests 1-6. The decimal guarantee of principle 4, asserted at the point
/// it enters the system.
/// </summary>
/// <remarks>
/// Amounts are written as strings and parsed rather than passed as
/// <c>[InlineData]</c> literals: an attribute argument cannot be a
/// <see cref="decimal"/> constant, so a bare <c>2.345</c> would arrive here as a
/// <see cref="double"/> — already rounded by the compiler, and testing the opposite
/// of what this file is for.
/// </remarks>
public sealed class MoneyTests
{
    private const string Brl = "BRL";
    private const string Usd = "USD";

    /// <summary>Spec unit test 1.</summary>
    [Theory]
    [InlineData("2.345", "2.34")]
    [InlineData("2.355", "2.36")]
    [InlineData("-2.345", "-2.34")]
    [InlineData("-2.355", "-2.36")]
    [InlineData("2.344", "2.34")]
    [InlineData("2.346", "2.35")]
    [InlineData("42.90", "42.90")]
    public void Construction_rounds_to_two_places_to_even(string input, string expected)
    {
        var money = new Money(Decimal(input), Brl);

        Assert.Equal(Decimal(expected), money.Amount);
    }

    /// <summary>Spec unit test 2.</summary>
    [Theory]
    [InlineData("brl")]
    [InlineData("Brl")]
    [InlineData("BR")]
    [InlineData("BRLX")]
    [InlineData("B2L")]
    [InlineData("   ")]
    [InlineData("")]
    [InlineData(null)]
    public void Rejects_anything_that_is_not_a_three_letter_uppercase_code(string? currency) =>
        Assert.ThrowsAny<ArgumentException>(() => new Money(1m, currency!));

    [Fact]
    public void Accepts_a_three_letter_uppercase_code()
    {
        Assert.Equal(Brl, new Money(1m, Brl).Currency);
        Assert.Equal(Usd, new Money(1m, Usd).Currency);
    }

    /// <summary>Spec unit test 3.</summary>
    [Fact]
    public void Adds_and_subtracts_within_one_currency()
    {
        var ten = new Money(10.50m, Brl);
        var three = new Money(3.25m, Brl);

        Assert.Equal(new Money(13.75m, Brl), ten + three);
        Assert.Equal(new Money(7.25m, Brl), ten - three);

        // The sign is the source of truth for direction, so adding an expense to an
        // income is ordinary arithmetic rather than a special case.
        Assert.Equal(new Money(-1.00m, Brl), new Money(2.50m, Brl) + new Money(-3.50m, Brl));
    }

    /// <summary>Spec unit test 4.</summary>
    [Fact]
    public void Refuses_to_add_or_subtract_across_currencies()
    {
        var brl = new Money(10m, Brl);
        var usd = new Money(10m, Usd);

        Assert.Throws<InvalidOperationException>(() => brl + usd);
        Assert.Throws<InvalidOperationException>(() => brl - usd);
    }

    /// <summary>Spec unit test 5.</summary>
    [Fact]
    public void Refuses_to_order_across_currencies()
    {
        var brl = new Money(10m, Brl);
        var usd = new Money(10m, Usd);

        Assert.Throws<InvalidOperationException>(() => brl < usd);
        Assert.Throws<InvalidOperationException>(() => brl > usd);
        Assert.Throws<InvalidOperationException>(() => brl <= usd);
        Assert.Throws<InvalidOperationException>(() => brl >= usd);
    }

    [Fact]
    public void Orders_within_one_currency()
    {
        var small = new Money(1.00m, Brl);
        var large = new Money(2.00m, Brl);

        Assert.True(small < large);
        Assert.True(large > small);
        Assert.True(small <= new Money(1.00m, Brl));
        Assert.True(small >= new Money(1.00m, Brl));
    }

    /// <summary>
    /// Equality is value equality and does not throw across currencies: two amounts in
    /// different currencies are not equal, which is an answer, not an error. Only
    /// ordering and arithmetic have no meaning across currencies.
    /// </summary>
    [Fact]
    public void Compares_equal_only_within_the_same_currency()
    {
        Assert.Equal(new Money(10m, Brl), new Money(10m, Brl));
        Assert.NotEqual(new Money(10m, Usd), new Money(10m, Brl));
    }

    /// <summary>
    /// Spec unit test 6 — the one that proves the chain is decimal and not float.
    /// </summary>
    [Fact]
    public void A_tenth_plus_two_tenths_is_exactly_three_tenths()
    {
        var sum = new Money(0.1m, Brl) + new Money(0.2m, Brl);

        Assert.Equal(new Money(0.3m, Brl), sum);
        Assert.Equal(0.3m, sum.Amount);

        // The same sum in binary floating point misses by 5.5e-17. This line is the
        // reason Money exists, the column is numeric(18,2), and CLAUDE.md forbids
        // double even in a local.
        Assert.NotEqual(0.3d, 0.1d + 0.2d);
    }

    private static decimal Decimal(string value) =>
        decimal.Parse(value, CultureInfo.InvariantCulture);
}
