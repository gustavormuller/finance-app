namespace Finance.Api.Domain.Investments;

/// <summary>What a <see cref="Movement"/> did to a position (007, decision 2).</summary>
/// <remarks>
/// Stored as <c>int</c>, with the values written down: renumbering them later would
/// silently reinterpret every movement. Amortization is absent on purpose; it arrives
/// with fixed income and FII capital returns.
/// </remarks>
public enum MovementKind
{
    Buy = 0,
    Sell = 1,
    Dividend = 2,
    Jcp = 3,

    /// <summary>
    /// A zero-cost quantity movement: a 1:2 split on 100 shares is <c>Quantity = 100</c>,
    /// <c>UnitPrice = 0</c> (decision 3). No ratio.
    /// </summary>
    Split = 4,
}
