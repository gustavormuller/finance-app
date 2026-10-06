using Finance.Api.Domain.Investments;
using Finance.Api.Domain.Transactions;

namespace Finance.Api.Domain.Returns;

/// <summary>
/// The cash a movement moves, and its conversion to BRL: the one set of rules that both
/// TWR (<see cref="ReturnSeries"/>) and XIRR (<see cref="MoneyWeightedReturn.FlowsInBrl"/>) read
/// (008, decisions 3 and 4).
/// </summary>
internal static class MovementCash
{
    /// <summary>
    /// In the movement's currency, unrounded. <c>Inflow</c> is cash into the position: a
    /// buy's <c>quantity x price + fees</c>, less a sell's <c>quantity x price - fees</c>.
    /// <c>Income</c> is a dividend's or JCP's <c>amount - fees</c>. A split is neither.
    /// </summary>
    public static (decimal Inflow, decimal Income) Of(Movement movement) => movement.Kind switch
    {
        MovementKind.Buy => (movement.Quantity * movement.UnitPrice + movement.Fees, 0m),
        MovementKind.Sell => (-(movement.Quantity * movement.UnitPrice - movement.Fees), 0m),
        MovementKind.Dividend or MovementKind.Jcp => (0m, movement.Amount - movement.Fees),
        _ => (0m, 0m),
    };

    /// <summary>
    /// <paramref name="amount"/> of <paramref name="currency"/> on <paramref name="date"/> in
    /// BRL, rounded to cents. The rate follows 007's rule: the latest of
    /// <paramref name="orderedRates"/> on or before the date, else the earliest.
    /// </summary>
    /// <param name="orderedRates">BRL per unit of <paramref name="currency"/>, in date order; ignored for BRL.</param>
    public static decimal InBrl(string currency, IReadOnlyList<DailyPoint> orderedRates, DateOnly date, decimal amount) =>
        new Money(amount * RateOn(currency, orderedRates, date), SnapshotBuilder.BaseCurrency).Amount;

    private static decimal RateOn(string currency, IReadOnlyList<DailyPoint> orderedRates, DateOnly date)
    {
        if (currency == SnapshotBuilder.BaseCurrency)
        {
            return 1m;
        }

        if (orderedRates.Count == 0)
        {
            throw new InvalidOperationException($"No BRL rate for {currency} to convert a flow on {date}.");
        }

        var onOrBefore = -1;
        for (var i = 0; i < orderedRates.Count && orderedRates[i].Date <= date; i++)
        {
            onOrBefore = i;
        }

        return orderedRates[onOrBefore < 0 ? 0 : onOrBefore].Value;
    }
}
