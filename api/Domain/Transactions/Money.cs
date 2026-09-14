namespace Finance.Api.Domain.Transactions;

/// <summary>
/// An amount of money in one currency. The project's principle 4 — money is
/// <see cref="decimal"/>, never <c>float</c> — starts here and is carried by this
/// type all the way to the <c>numeric(18,2)</c> column.
/// </summary>
/// <remarks>
/// A value object, not an entity: no identity, no <c>Id</c>, and no EF configuration
/// of its own beyond the <c>ComplexProperty</c> that maps it to two columns
/// (ARCHITECTURE.md §4, ADR-014).
/// <para>
/// It exists to stop BRL being added to USD by accident. That bug is invisible today,
/// with only BRL accounts, and arrives with US stocks and crypto in 007.
/// </para>
/// </remarks>
public readonly record struct Money
{
    /// <param name="amount">Rounded to two decimal places, <see cref="MidpointRounding.ToEven"/>.</param>
    /// <param name="currency">A three-letter uppercase ISO 4217 code.</param>
    public Money(decimal amount, string currency) => throw new NotImplementedException();

    public decimal Amount { get; }

    public string Currency { get; }

    public static Money operator +(Money left, Money right) => throw new NotImplementedException();

    public static Money operator -(Money left, Money right) => throw new NotImplementedException();

    public static bool operator <(Money left, Money right) => throw new NotImplementedException();

    public static bool operator >(Money left, Money right) => throw new NotImplementedException();

    public static bool operator <=(Money left, Money right) => throw new NotImplementedException();

    public static bool operator >=(Money left, Money right) => throw new NotImplementedException();

    public override string ToString() => throw new NotImplementedException();
}
