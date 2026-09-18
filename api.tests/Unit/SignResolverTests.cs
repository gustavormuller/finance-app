using Finance.Api.Domain.Import;

namespace Finance.Api.Tests.Unit;

/// <summary>Spec unit tests 14-17.</summary>
public sealed class SignResolverTests
{
    /// <summary>Spec unit test 14.</summary>
    [Theory]
    [InlineData("-42.90", "-42.90")]
    [InlineData("3000", "3000")]
    [InlineData("0", "0")]
    public void Signed_passes_the_value_through(string amount, string expected)
    {
        var resolution = SignResolver.Resolve(SignMode.Signed, Decimal(amount), debit: null, credit: null);

        Assert.Equal(SignProblem.None, resolution.Problem);
        Assert.Equal(Decimal(expected), resolution.Amount);
    }

    /// <summary>Spec unit test 15. A card statement lists a purchase as +42.90.</summary>
    [Theory]
    [InlineData("42.90", "-42.90")]
    [InlineData("-150", "150")]
    [InlineData("0", "0")]
    public void SignedInverted_negates(string amount, string expected)
    {
        var resolution = SignResolver.Resolve(SignMode.SignedInverted, Decimal(amount), debit: null, credit: null);

        Assert.Equal(SignProblem.None, resolution.Problem);
        Assert.Equal(Decimal(expected), resolution.Amount);
    }

    /// <summary>
    /// Spec unit test 16. Debit is money leaving whatever sign the bank printed it
    /// with: some sheets show <c>150,00</c> under Débito, some <c>-150,00</c>.
    /// </summary>
    [Theory]
    [InlineData("150", null, "-150")]
    [InlineData("-150", null, "-150")]
    [InlineData(null, "3000", "3000")]
    [InlineData(null, "-3000", "3000")]
    [InlineData("150", "0", "-150")]
    [InlineData("0", "3000", "3000")]
    public void DebitCredit_maps_each_side(string? debit, string? credit, string expected)
    {
        var resolution = SignResolver.Resolve(SignMode.DebitCredit, amount: null, Decimal(debit), Decimal(credit));

        Assert.Equal(SignProblem.None, resolution.Problem);
        Assert.Equal(Decimal(expected), resolution.Amount);
    }

    /// <summary>Spec unit test 17.</summary>
    [Fact]
    public void DebitCredit_with_both_sides_populated_is_a_problem()
    {
        var resolution = SignResolver.Resolve(SignMode.DebitCredit, amount: null, debit: 150m, credit: 3000m);

        Assert.Equal(SignProblem.DebitAndCredit, resolution.Problem);
        Assert.Null(resolution.Amount);
    }

    [Theory]
    [InlineData(SignMode.Signed)]
    [InlineData(SignMode.SignedInverted)]
    [InlineData(SignMode.DebitCredit)]
    public void No_value_at_all_is_a_missing_amount(SignMode mode)
    {
        var resolution = SignResolver.Resolve(mode, amount: null, debit: null, credit: null);

        Assert.Equal(SignProblem.MissingAmount, resolution.Problem);
        Assert.Null(resolution.Amount);
    }

    [Fact]
    public void DebitCredit_with_both_sides_zero_is_a_missing_amount()
    {
        var resolution = SignResolver.Resolve(SignMode.DebitCredit, amount: null, debit: 0m, credit: 0m);

        Assert.Equal(SignProblem.MissingAmount, resolution.Problem);
    }

    /// <summary>
    /// The debit and credit columns are ignored under the signed modes, so a template
    /// that happens to carry stale column names cannot change the result.
    /// </summary>
    [Fact]
    public void Signed_modes_ignore_the_debit_and_credit_columns()
    {
        var resolution = SignResolver.Resolve(SignMode.Signed, amount: -10m, debit: 999m, credit: 999m);

        Assert.Equal(-10m, resolution.Amount);
    }

    private static decimal? Decimal(string? text) =>
        text is null ? null : decimal.Parse(text, System.Globalization.CultureInfo.InvariantCulture);
}
