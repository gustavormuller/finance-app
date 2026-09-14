namespace Finance.Api.Domain.Transactions;

/// <summary>
/// An amount of money in one currency. The project's principle 4 — money is
/// <see cref="decimal"/>, never <c>float</c> — starts here and is carried by this
/// type unchanged to the <c>numeric(18,2)</c> column.
/// </summary>
/// <remarks>
/// A value object, not an entity: no identity, no <c>Id</c>, and no EF configuration
/// of its own beyond the <c>ComplexProperty</c> that maps it to two columns
/// (ARCHITECTURE.md §4, ADR-014).
/// <para>
/// It exists to stop BRL being added to USD by accident. That bug is invisible today,
/// with only BRL accounts, and arrives with US stocks and crypto in 007.
/// </para>
/// <para>
/// Being a struct, <c>default(Money)</c> bypasses the constructor and yields a zero
/// amount with a null currency. Nothing constructs one: the API builds every instance
/// from a request and EF Core rebuilds it through the constructor below.
/// </para>
/// </remarks>
public readonly record struct Money(decimal Amount, string Currency)
{
    /// <summary>
    /// Two decimal places, <see cref="MidpointRounding.ToEven"/>, fixed at
    /// construction so no amount anywhere in the system carries a third digit the
    /// column could not store.
    /// </summary>
    public decimal Amount { get; } = Math.Round(Amount, 2, MidpointRounding.ToEven);

    /// <summary>A three-letter uppercase ISO 4217 code.</summary>
    public string Currency { get; } = RequireIsoCode(Currency);

    public static Money operator +(Money left, Money right) =>
        new(left.Amount + SameCurrencyAs(left, right, "add").Amount, left.Currency);

    public static Money operator -(Money left, Money right) =>
        new(left.Amount - SameCurrencyAs(left, right, "subtract").Amount, left.Currency);

    public static bool operator <(Money left, Money right) => Compare(left, right) < 0;

    public static bool operator >(Money left, Money right) => Compare(left, right) > 0;

    public static bool operator <=(Money left, Money right) => Compare(left, right) <= 0;

    public static bool operator >=(Money left, Money right) => Compare(left, right) >= 0;

    private static string RequireIsoCode(string currency)
    {
        ArgumentNullException.ThrowIfNull(currency);

        // Rejected rather than upper-cased: a lowercase code reaching this constructor
        // means something upstream is not normalising, and silently fixing it here
        // would hide that until two spellings of the same currency stopped comparing
        // equal.
        if (currency.Length != 3 || !currency.All(char.IsAsciiLetterUpper))
        {
            throw new ArgumentException(
                $"'{currency}' is not a three-letter uppercase ISO 4217 code.",
                nameof(currency));
        }

        return currency;
    }

    /// <summary>
    /// Ordering is only defined within a currency. Comparing 10 BRL against 10 USD has
    /// no answer without an exchange rate, and returning one anyway is the bug this
    /// type exists to prevent.
    /// </summary>
    private static int Compare(Money left, Money right) =>
        left.Amount.CompareTo(SameCurrencyAs(left, right, "compare").Amount);

    private static Money SameCurrencyAs(Money left, Money right, string operation) =>
        left.Currency == right.Currency
            ? right
            : throw new InvalidOperationException(
                $"Cannot {operation} {left.Currency} and {right.Currency}.");
}
