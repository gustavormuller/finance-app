using Finance.Api.Domain.MarketData;
using Finance.Api.Domain.Transactions;

namespace Finance.Api.Domain.Investments;

/// <summary>
/// Builds <see cref="PortfolioDaily"/> rows for one asset (007, "SnapshotBuilder"): one
/// per calendar day, the last close carried over non-trading days. Pure over its
/// inputs, which the caller loads; no EF, no clock (the range end is an argument).
/// </summary>
/// <remarks>
/// <para>
/// <b>Rounding happens here and nowhere else.</b> The position is carried at full
/// <see cref="decimal"/> precision by <see cref="PositionCalculator"/>. Each value is
/// rounded only when it becomes a column of fixed scale: <c>AverageCost</c> to 8
/// places, <c>ValueBrl</c> and <c>CostBasisBrl</c> to 2 through <see cref="Money"/>.
/// Both round half to even, the rule <c>Money</c> already uses. Quantity, price and
/// rate are copied as they are, since their inputs already have the column's scale.
/// </para>
/// <para>
/// A day is skipped, not zero-valued, when the asset has no close yet, or when a
/// non-BRL asset has no FX rate yet (decision/test 12).
/// </para>
/// </remarks>
public static class SnapshotBuilder
{
    /// <summary>The currency whose FX rate is 1 and that the rows are valued in.</summary>
    public const string BaseCurrency = "BRL";

    private const int AverageCostPlaces = 8;

    /// <summary>The rows from <paramref name="from"/>, or the first movement if later, to <paramref name="to"/>.</summary>
    /// <param name="currency">The market asset's currency; FX is applied unless it is BRL.</param>
    /// <param name="movements">The asset's <b>whole</b> history, not only what follows <paramref name="from"/>.</param>
    /// <param name="prices">
    /// Closes of this asset, sparse. To start at <paramref name="from"/>, the list needs
    /// the latest close on or before it.
    /// </param>
    /// <param name="fxRates">
    /// BRL per unit of <paramref name="currency"/> (USDBRL for USD), sparse. Needs the
    /// rate on or before each buy and on or before <paramref name="from"/>. Ignored for
    /// BRL.
    /// </param>
    public static IReadOnlyList<PortfolioDaily> Build(
        Guid userId,
        Guid assetId,
        string currency,
        IEnumerable<Movement> movements,
        IEnumerable<Price> prices,
        IEnumerable<Benchmark> fxRates,
        DateOnly from,
        DateOnly to)
    {
        var ordered = PositionCalculator.InOrder(movements).ToList();
        var closes = prices.OrderBy(p => p.Date).ToList();
        var rates = fxRates.OrderBy(b => b.Date).ToList();
        var isBase = currency == BaseCurrency;

        if (ordered.Count == 0 || closes.Count == 0 || (!isBase && rates.Count == 0))
        {
            return [];
        }

        // A buy dated before the first known rate takes the earliest one after it.
        // USDBRL at BCB goes back decades, so this only bites a fresh catalogue whose
        // history is not backfilled yet.
        decimal RateForCost(DateOnly date) =>
            isBase ? 1m : (rates.LastOrDefault(r => r.Date <= date) ?? rates[0]).Value;

        var rows = new List<PortfolioDaily>();
        var position = default(Position);
        var start = ordered[0].Date > from ? ordered[0].Date : from;
        int applied = 0, close = -1, rate = -1;

        // Walks from the first movement, not from `from`, so the position on `from`
        // includes everything before it.
        for (var day = ordered[0].Date; day <= to; day = day.AddDays(1))
        {
            for (; applied < ordered.Count && ordered[applied].Date <= day; applied++)
            {
                var movement = ordered[applied];
                position = PositionCalculator.Apply(position, movement, RateForCost(movement.Date));
            }

            while (close + 1 < closes.Count && closes[close + 1].Date <= day)
            {
                close++;
            }

            while (rate + 1 < rates.Count && rates[rate + 1].Date <= day)
            {
                rate++;
            }

            if (day < start || close < 0 || (!isBase && rate < 0))
            {
                continue;
            }

            var price = closes[close];
            var fxRate = isBase ? 1m : rates[rate].Value;

            rows.Add(new PortfolioDaily
            {
                UserId = userId,
                AssetId = assetId,
                Date = day,
                Quantity = position.Quantity,
                AverageCost = Math.Round(position.AverageCost, AverageCostPlaces, MidpointRounding.ToEven),
                Price = price.Close,
                PriceDate = price.Date,
                FxRate = fxRate,
                ValueBrl = Brl(position.Quantity * price.Close * fxRate),
                CostBasisBrl = Brl(position.CostBasisBrl),
            });
        }

        return rows;
    }

    /// <summary>Totals become <see cref="Money"/> at the boundary (decision 10), and take its rounding.</summary>
    private static decimal Brl(decimal amount) => new Money(amount, BaseCurrency).Amount;
}
