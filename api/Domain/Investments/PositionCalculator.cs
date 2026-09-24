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
/// Average-cost position arithmetic (007, "PositionCalculator"; decision 4, preço
/// médio, not FIFO). Pure: no EF, no clock.
/// </summary>
/// <remarks>
/// The spec's formulas are applied as written, with the average carried as state, so a
/// sell leaves it untouched to the last digit. Nothing is rounded here. Every
/// intermediate keeps <see cref="decimal"/>'s 28 significant digits, and the only
/// rounding is at the column boundary. A broker computes preço médio as total cost over
/// quantity and rounds for display; rounding at each step would drift from it.
/// </remarks>
public static class PositionCalculator
{
    /// <summary>
    /// The order that decides the position on a date: by date, then by creation
    /// (007, validation rules). Stable, so ties beyond that keep their input order.
    /// </summary>
    public static IEnumerable<Movement> InOrder(IEnumerable<Movement> movements) =>
        movements.OrderBy(m => m.Date).ThenBy(m => m.CreatedAt);

    /// <summary>The position after each movement, in <see cref="InOrder"/> order.</summary>
    /// <param name="fxOn">
    /// BRL per unit of the asset's currency on a date, used for the cost basis of each
    /// buy. Omitted, every rate is 1: right for a BRL asset, native for any other.
    /// </param>
    /// <exception cref="InvalidOperationException">A sell exceeds the position.</exception>
    public static IReadOnlyList<PositionStep> Calculate(
        IEnumerable<Movement> movements,
        Func<DateOnly, decimal>? fxOn = null)
    {
        var steps = new List<PositionStep>();
        var position = default(Position);

        foreach (var movement in InOrder(movements))
        {
            position = Apply(position, movement, fxOn?.Invoke(movement.Date) ?? 1m);
            steps.Add(new PositionStep(movement, position));
        }

        return steps;
    }

    /// <summary>One movement's effect on a position.</summary>
    /// <param name="fxRate">The rate on the movement's date; only a buy uses it.</param>
    /// <exception cref="InvalidOperationException">
    /// A sell exceeds the position. <see cref="MovementRules"/> refuses that history
    /// before it is stored, so reaching this is a bug, not a user error.
    /// </exception>
    public static Position Apply(Position position, Movement movement, decimal fxRate) =>
        movement.Kind switch
        {
            MovementKind.Buy => Buy(position, movement, fxRate),
            MovementKind.Sell => Sell(position, movement),
            MovementKind.Split => Split(position, movement),

            // Net of fees: Amount is the cash received and a fee on it is cash not
            // received, as fees come off a sell's proceeds (decision 5).
            MovementKind.Dividend or MovementKind.Jcp =>
                position with { Income = position.Income + movement.Amount - movement.Fees },
            _ => throw new ArgumentOutOfRangeException(nameof(movement), movement.Kind, "Unknown movement kind."),
        };

    /// <summary><c>newAvg = (qty x avg + buyQty x buyPrice + fees) / (qty + buyQty)</c>.</summary>
    private static Position Buy(Position position, Movement buy, decimal fxRate)
    {
        var cost = buy.Quantity * buy.UnitPrice + buy.Fees;
        var quantity = position.Quantity + buy.Quantity;

        return position with
        {
            Quantity = quantity,
            AverageCost = (position.Quantity * position.AverageCost + cost) / quantity,
            CostBasisBrl = position.CostBasisBrl + cost * fxRate,
        };
    }

    /// <summary>
    /// Average unchanged; realised <c>(sellPrice x sellQty - fees) - avg x sellQty</c>.
    /// The BRL cost basis shrinks by the fraction of units sold.
    /// </summary>
    private static Position Sell(Position position, Movement sell)
    {
        if (sell.Quantity > position.Quantity)
        {
            throw new InvalidOperationException(
                $"Sell of {sell.Quantity} on {sell.Date:yyyy-MM-dd} exceeds the position of {position.Quantity}.");
        }

        var quantity = position.Quantity - sell.Quantity;
        var realised = (sell.UnitPrice * sell.Quantity - sell.Fees) - position.AverageCost * sell.Quantity;

        // A closed position is reset outright, so the next buy starts from nothing
        // and no residue of the division below survives.
        return quantity == 0m
            ? position with { Quantity = 0m, AverageCost = 0m, CostBasisBrl = 0m, RealisedGain = position.RealisedGain + realised }
            : position with
            {
                Quantity = quantity,
                CostBasisBrl = position.CostBasisBrl * quantity / position.Quantity,
                RealisedGain = position.RealisedGain + realised,
            };
    }

    /// <summary>
    /// <c>newAvg = (qty x avg) / (qty + splitQty)</c>. Zero cost: the BRL cost basis
    /// does not move, and fees on a split are ignored, as the spec's formula has none.
    /// </summary>
    private static Position Split(Position position, Movement split)
    {
        var quantity = position.Quantity + split.Quantity;

        return position with
        {
            Quantity = quantity,
            AverageCost = quantity == 0m ? 0m : position.Quantity * position.AverageCost / quantity,
        };
    }
}
