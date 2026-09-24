namespace Finance.Api.Domain.Returns;

/// <summary>
/// Exponential, logarithm and real powers in <see cref="decimal"/>. The returns module
/// needs fractional powers (annualising, the XIRR discount, a monthly share of an
/// annual spread) and 008's decision 10 keeps it in <c>decimal</c> throughout, so
/// <see cref="Math.Pow(double, double)"/> is not available to it.
/// </summary>
public static class DecimalMath
{
    public static decimal Exp(decimal x) => throw new NotImplementedException();

    public static decimal Ln(decimal x) => throw new NotImplementedException();

    public static decimal Pow(decimal value, decimal exponent) => throw new NotImplementedException();
}
