using Finance.Api.Domain.Returns;

namespace Finance.Api.Domain.Compare;

/// <summary>
/// A series in reais or in dollars at the day's PTAX (026, decision 10). PTAX is reais per
/// dollar, BCB business days only, so on a day without a quote the last one applies.
/// </summary>
/// <remarks>
/// The result has a point on every date either input has, from the first date on which both
/// have begun: a dollar series moves in reais on a day PTAX moves even when its own market
/// is closed. Every rate must be positive.
/// </remarks>
public static class PtaxConversion
{
    /// <summary>A dollar series in reais: each value times the PTAX carried to its date.</summary>
    public static IReadOnlyList<DailyPoint> UsdToBrl(IReadOnlyList<DailyPoint> usd, IReadOnlyList<DailyPoint> brlPerUsd) =>
        Convert(usd, brlPerUsd, (value, rate) => value * rate);

    /// <summary>A real series in dollars: each value over the PTAX carried to its date.</summary>
    public static IReadOnlyList<DailyPoint> BrlToUsd(IReadOnlyList<DailyPoint> brl, IReadOnlyList<DailyPoint> brlPerUsd) =>
        Convert(brl, brlPerUsd, (value, rate) => value / rate);

    private static List<DailyPoint> Convert(
        IReadOnlyList<DailyPoint> values, IReadOnlyList<DailyPoint> rates, Func<decimal, decimal, decimal> convert)
    {
        var orderedValues = values.OrderBy(point => point.Date).ToList();
        var orderedRates = rates.OrderBy(point => point.Date).ToList();
        var dates = orderedValues.Select(point => point.Date).Concat(orderedRates.Select(point => point.Date)).Distinct().Order();

        var converted = new List<DailyPoint>();
        int nextValue = 0, nextRate = 0;
        decimal? value = null, rate = null;
        foreach (var date in dates)
        {
            while (nextValue < orderedValues.Count && orderedValues[nextValue].Date <= date)
            {
                value = orderedValues[nextValue++].Value;
            }

            while (nextRate < orderedRates.Count && orderedRates[nextRate].Date <= date)
            {
                rate = orderedRates[nextRate++].Value;
            }

            if (value is { } amount && rate is { } quote)
            {
                converted.Add(new DailyPoint(date, convert(amount, quote)));
            }
        }

        return converted;
    }
}
