namespace Finance.Api.Domain.Import;

/// <summary>
/// How a CSV template says which way the money went.
/// </summary>
/// <remarks>
/// Stored as <c>int</c>, with the values written down: renumbering them later would
/// silently reinterpret every saved template.
/// </remarks>
public enum SignMode
{
    /// <summary>The amount column carries the sign, negative leaving.</summary>
    Signed = 0,

    /// <summary>The amount column carries the opposite sign: card statements list purchases as positive.</summary>
    SignedInverted = 1,

    /// <summary>Two columns, one for money leaving and one for money arriving.</summary>
    DebitCredit = 2,
}
