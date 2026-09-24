namespace Finance.Api.Domain.Investments;

/// <summary>
/// One asset's position after a movement. Full <see cref="decimal"/> precision: nothing
/// here is rounded. Rounding happens once, where a value is written to a column of
/// fixed scale (<see cref="SnapshotBuilder"/>).
/// </summary>
/// <param name="Quantity">Units held.</param>
/// <param name="AverageCost">Per unit, native currency, fees included (preço médio).</param>
/// <param name="CostBasisBrl">
/// What the units held cost, each buy converted at its own FX rate and reduced
/// proportionally by sells. In BRL when the caller supplies FX rates. With the default
/// rate of 1 it is the native cost basis.
/// </param>
/// <param name="RealisedGain">Native currency, net of sell fees, accumulated.</param>
/// <param name="Income">Dividends and JCP received, native currency, accumulated.</param>
public readonly record struct Position(
    decimal Quantity,
    decimal AverageCost,
    decimal CostBasisBrl,
    decimal RealisedGain,
    decimal Income);

/// <summary>A movement and the position right after it.</summary>
public readonly record struct PositionStep(Movement Movement, Position Position);

/// <summary>
/// Average-cost position arithmetic (007, "PositionCalculator"). Pure: no EF, no clock.
/// </summary>
public static class PositionCalculator
{
    public static IReadOnlyList<PositionStep> Calculate(
        IEnumerable<Movement> movements,
        Func<DateOnly, decimal>? fxOn = null) =>
        throw new NotImplementedException();
}
