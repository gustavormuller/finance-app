using Finance.Api.Domain.Investments;
using Finance.Api.Domain.Transactions;

namespace Finance.Api.Domain.Returns;

/// <summary>
/// One asset's <see cref="ReturnDay"/>s from its <see cref="PortfolioDaily"/> rows and its
/// movements, in BRL or in the asset's own currency (008, decision 3).
/// </summary>
/// <remarks>
/// <para>
/// A buy is an inflow of <c>quantity x price + fees</c>, a sell an outflow of
/// <c>quantity x price - fees</c>, a dividend or JCP income of <c>amount - fees</c>. A
/// split is neither. Each amount is cash, so it is rounded to cents as <see cref="Money"/>.
/// </para>
/// <para>
/// A movement dated on or before the first row lands on the first row: a buy before the
/// first close would otherwise send the cash out one day and bring the value in another.
/// The first row is the base day of an asset on its own, so there the flow does not count;
/// in a portfolio that started earlier, it offsets the value the asset brings in. A
/// movement after the last row is outside the period and left out.
/// </para>
/// </remarks>
public static class ReturnSeries
{
    /// <summary>
    /// Values are <c>ValueBrl</c>. A flow in another currency converts at 007's rule: the
    /// latest of <paramref name="fxRates"/> on or before its own date, else the earliest.
    /// </summary>
    /// <param name="fxRates">BRL per unit of <paramref name="currency"/>; ignored for BRL.</param>
    public static IReadOnlyList<ReturnDay> InBrl(
        string currency, IReadOnlyList<PortfolioDaily> rows, IEnumerable<Movement> movements, IReadOnlyList<DailyPoint> fxRates)
    {
        var rates = fxRates.OrderBy(rate => rate.Date).ToList();
        return Build(rows, row => row.ValueBrl, movements, (movement, amount) =>
            MovementCash.InBrl(currency, rates, movement.Date, amount));
    }

    /// <summary>Values are <c>Quantity x Price</c> and flows stay in the asset's currency.</summary>
    public static IReadOnlyList<ReturnDay> InNative(IReadOnlyList<PortfolioDaily> rows, IEnumerable<Movement> movements) =>
        Build(rows, row => row.Quantity * row.Price, movements, (movement, amount) => new Money(amount, movement.Currency).Amount);

    private static List<ReturnDay> Build(
        IReadOnlyList<PortfolioDaily> rows,
        Func<PortfolioDaily, decimal> value,
        IEnumerable<Movement> movements,
        Func<Movement, decimal, decimal> cash)
    {
        if (rows.Count == 0)
        {
            return [];
        }

        var ordered = rows.OrderBy(row => row.Date).ToList();
        var first = ordered[0].Date;
        var last = ordered[^1].Date;
        var income = new Dictionary<DateOnly, decimal>();
        var flow = new Dictionary<DateOnly, decimal>();

        foreach (var movement in movements.Where(movement => movement.Date <= last))
        {
            var date = movement.Date < first ? first : movement.Date;
            var (inflow, received) = MovementCash.Of(movement);
            if (inflow != 0m)
            {
                Add(flow, date, cash(movement, inflow));
            }

            if (received != 0m)
            {
                Add(income, date, cash(movement, received));
            }
        }

        return ordered
            .Select(row => new ReturnDay(row.Date, value(row), income.GetValueOrDefault(row.Date), flow.GetValueOrDefault(row.Date)))
            .ToList();
    }

    private static void Add(Dictionary<DateOnly, decimal> sums, DateOnly date, decimal amount) =>
        sums[date] = sums.GetValueOrDefault(date) + amount;
}
