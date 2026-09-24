namespace Finance.Api.Domain.Returns;

/// <summary>
/// Exponential, logarithm and real powers in <see cref="decimal"/>. The returns module
/// needs fractional powers (annualising, the XIRR discount, a monthly share of an
/// annual spread) and 008's decision 10 keeps it in <c>decimal</c> throughout, so
/// <see cref="Math.Pow(double, double)"/> is not available to it.
/// </summary>
/// <remarks>
/// Results agree with a 40-digit reference to about 27 significant digits, or to
/// <c>1e-27</c> for values that small (the type keeps 28 places after the point).
/// Series stop when the next term no longer changes the sum.
/// </remarks>
public static class DecimalMath
{
    /// <summary>e, rounded to the 28 places the type holds.</summary>
    private const decimal E = 2.7182818284590452353602874714m;

    /// <summary>ln 2, rounded to the 28 places the type holds.</summary>
    private const decimal Ln2 = 0.6931471805599453094172321215m;

    /// <summary>The largest argument whose exponential is below <see cref="decimal.MaxValue"/> (ln of it is 66.54).</summary>
    private const decimal MaxExpArgument = 66.5m;

    /// <summary>
    /// e raised to <paramref name="x"/>. Throws <see cref="OverflowException"/> past
    /// <see cref="decimal.MaxValue"/>; an argument below -66.5 underflows to zero.
    /// </summary>
    public static decimal Exp(decimal x)
    {
        if (x == 0m)
        {
            return 1m;
        }

        if (x < 0m)
        {
            return -x > MaxExpArgument ? 0m : 1m / Exp(-x);
        }

        if (x > MaxExpArgument)
        {
            throw new OverflowException($"exp({x}) is past the decimal range.");
        }

        // e^x = e^n * e^f, with n whole and f in [0, 1), where the series converges fast.
        var whole = decimal.Truncate(x);
        var fraction = x - whole;

        var sum = 1m;
        var term = 1m;
        for (var k = 1; ; k++)
        {
            term = term * fraction / k;
            var next = sum + term;
            if (next == sum)
            {
                break;
            }

            sum = next;
        }

        return WholePower(E, (long)whole) * sum;
    }

    /// <summary>The natural logarithm of a positive <paramref name="x"/>.</summary>
    public static decimal Ln(decimal x)
    {
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(x);

        if (x == 1m)
        {
            return 0m;
        }

        // ln x = k ln 2 + ln y, with y brought into [0.75, 1.5] by halving or doubling.
        var halvings = 0;
        var y = x;
        while (y > 1.5m)
        {
            y /= 2m;
            halvings++;
        }

        while (y < 0.75m)
        {
            y *= 2m;
            halvings--;
        }

        // ln y = 2 atanh(z) = 2 (z + z^3/3 + z^5/5 + ...), z = (y - 1) / (y + 1), |z| <= 0.2.
        var z = (y - 1m) / (y + 1m);
        var zSquared = z * z;
        var sum = z;
        var power = z;
        for (var odd = 3; ; odd += 2)
        {
            power *= zSquared;
            var next = sum + power / odd;
            if (next == sum)
            {
                break;
            }

            sum = next;
        }

        return halvings * Ln2 + 2m * sum;
    }

    /// <summary>
    /// <paramref name="value"/> raised to <paramref name="exponent"/>. A whole exponent
    /// is repeated multiplication, exact where the type can hold the result; any other is
    /// <c>exp(exponent * ln value)</c> and needs a value that is not negative.
    /// </summary>
    public static decimal Pow(decimal value, decimal exponent)
    {
        if (exponent == decimal.Truncate(exponent) && Math.Abs(exponent) <= int.MaxValue)
        {
            return exponent < 0m
                ? 1m / WholePower(value, (long)-exponent)
                : WholePower(value, (long)exponent);
        }

        if (value == 0m && exponent > 0m)
        {
            return 0m;
        }

        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(value);
        return Exp(exponent * Ln(value));
    }

    /// <summary>Square and multiply.</summary>
    private static decimal WholePower(decimal value, long exponent)
    {
        var result = 1m;
        while (exponent > 0)
        {
            if ((exponent & 1) == 1)
            {
                result *= value;
            }

            exponent >>= 1;
            if (exponent > 0)
            {
                value *= value;
            }
        }

        return result;
    }
}
