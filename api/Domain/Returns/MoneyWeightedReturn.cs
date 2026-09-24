namespace Finance.Api.Domain.Returns;

/// <summary>
/// XIRR (008, decisions 4 and 5): the annual rate <c>r</c> at which
/// <c>sum of CF_i / (1 + r)^(t_i / 365) = 0</c>, <c>t_i</c> in days from the first flow
/// (ACT/365). Newton-Raphson from 10%; when it leaves <c>[-0.99, 10]</c>, meets a zero
/// slope or does not converge in 100 steps, bisection on that bracket. Pure.
/// </summary>
/// <remarks>
/// <para>
/// The NPV is evaluated scaled by a positive factor, so that no discount factor exceeds 1.
/// Unscaled, <c>(1 + r)^(-t/365)</c> at <c>r = -0.99</c> passes the <c>decimal</c> range
/// after about 14 years, and at <c>r = 10</c> the mirror image does after about 28 if the
/// flows are discounted to the last date instead (DEFERRED, 008 CP1). Scaling leaves the
/// NPV's sign, which is all bisection reads, and the ratio NPV / NPV', which is Newton's
/// step, unchanged.
/// </para>
/// <para>
/// A root outside the bracket, or no root at all, is <c>null</c> rather than an exception.
/// </para>
/// </remarks>
public static class MoneyWeightedReturn
{
    private const decimal Lower = -0.99m;
    private const decimal Upper = 10m;
    private const decimal Guess = 0.10m;
    private const decimal Tolerance = 0.00000001m;
    private const int NewtonSteps = 100;
    private const int BisectionSteps = 200;
    private const decimal DaysPerYear = 365m;

    /// <summary>
    /// The annualised rate, or <c>null</c>: fewer than two flows, flows all of one sign, or
    /// no root found in <c>[-0.99, 10]</c>. Flows may come in any order.
    /// </summary>
    /// <exception cref="ArgumentException">The flows are in more than one currency.</exception>
    public static Rate? Compute(IReadOnlyList<CashFlow> flows)
    {
        var terms = Terms(flows);
        return terms is null ? null : Newton(terms) ?? Bisection(terms);
    }

    /// <summary>Newton alone; <c>null</c> where <see cref="Compute"/> would fall back to bisection.</summary>
    internal static Rate? Newton(IReadOnlyList<CashFlow> flows) => Terms(flows) is { } terms ? Newton(terms) : null;

    /// <summary>Bisection alone on <c>[-0.99, 10]</c>.</summary>
    internal static Rate? Bisection(IReadOnlyList<CashFlow> flows) => Terms(flows) is { } terms ? Bisection(terms) : null;

    /// <summary>Each flow as years from the first one and an amount; <c>null</c> when no root can exist.</summary>
    private static Term[]? Terms(IReadOnlyList<CashFlow> flows)
    {
        if (flows.Select(flow => flow.Amount.Currency).Distinct().Count() > 1)
        {
            throw new ArgumentException("XIRR needs every flow in one currency.", nameof(flows));
        }

        if (flows.Count < 2 || !flows.Any(flow => flow.Amount.Amount > 0m) || !flows.Any(flow => flow.Amount.Amount < 0m))
        {
            return null;
        }

        var first = flows.Min(flow => flow.Date).DayNumber;
        return flows.Select(flow => new Term((flow.Date.DayNumber - first) / DaysPerYear, flow.Amount.Amount)).ToArray();
    }

    private static Rate? Newton(Term[] terms)
    {
        var rate = Guess;
        for (var step = 0; step < NewtonSteps; step++)
        {
            var (value, slope) = Npv(terms, rate);
            if (slope == 0m)
            {
                return null;
            }

            decimal next;
            try
            {
                next = rate - value / slope;
            }
            catch (OverflowException)
            {
                // A slope near zero: the step is past the decimal range, let alone the bracket.
                return null;
            }

            if (next < Lower || next > Upper)
            {
                return null;
            }

            if (Math.Abs(next - rate) < Tolerance)
            {
                return new Rate(next);
            }

            rate = next;
        }

        return null;
    }

    private static Rate? Bisection(Term[] terms)
    {
        decimal low = Lower, high = Upper;
        var lowSign = Math.Sign(Npv(terms, low).Value);
        var highSign = Math.Sign(Npv(terms, high).Value);
        if (lowSign == 0)
        {
            return new Rate(low);
        }

        if (highSign == 0)
        {
            return new Rate(high);
        }

        if (lowSign == highSign)
        {
            return null;
        }

        for (var step = 0; step < BisectionSteps; step++)
        {
            var middle = (low + high) / 2m;
            var sign = Math.Sign(Npv(terms, middle).Value);
            if (sign == 0 || (high - low) / 2m < Tolerance)
            {
                return new Rate(middle);
            }

            if (sign == lowSign)
            {
                low = middle;
            }
            else
            {
                high = middle;
            }
        }

        return new Rate((low + high) / 2m);
    }

    /// <summary>
    /// NPV at <paramref name="rate"/> and its derivative, both times the same positive
    /// factor. Each discount factor is <c>exp(-t ln(1 + r))</c>; the largest exponent is
    /// taken from all of them, so every one is at most <c>exp(0) = 1</c> and the smallest
    /// underflow harmlessly to zero.
    /// </summary>
    private static (decimal Value, decimal Slope) Npv(Term[] terms, decimal rate)
    {
        var growth = 1m + rate;
        var logGrowth = DecimalMath.Ln(growth);
        var largest = terms.Max(term => -term.Years * logGrowth);

        decimal value = 0m, slope = 0m;
        foreach (var term in terms)
        {
            var discounted = term.Amount * DecimalMath.Exp(-term.Years * logGrowth - largest);
            value += discounted;
            slope -= term.Years * discounted / growth;
        }

        return (value, slope);
    }

    private readonly record struct Term(decimal Years, decimal Amount);
}
