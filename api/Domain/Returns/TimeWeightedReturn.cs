namespace Finance.Api.Domain.Returns;

/// <summary>A time-weighted return over a period, and its index for the chart.</summary>
/// <param name="Total">The whole period, not annualised.</param>
/// <param name="Annualised">
/// <c>(1 + Total)^(365 / Days) - 1</c>, computed for any period, a year or less included
/// (spec silent; DEFERRED, 008 CP2). <c>null</c> when there is no day to annualise over,
/// or when the result is past the <see cref="decimal"/> range.
/// </param>
/// <param name="Days">Calendar days from the base day to the last day.</param>
/// <param name="Index"><c>100 x (1 + r)</c> chained up to each day; the base day is exactly 100.</param>
public sealed record TwrResult(Rate Total, Rate? Annualised, int Days, IReadOnlyList<DailyPoint> Index);

/// <summary>
/// Daily-linked TWR (008, decision 1): <c>r_d = (V_d + D_d - F_d) / V_(d-1) - 1</c>,
/// <c>TWR = product of (1 + r_d) - 1</c>. Pure; the caller builds the days.
/// </summary>
/// <remarks>
/// The first day is the base day. Its own return is never computed, so a flow or income
/// on it does not count; its value is where the chain starts. A day whose previous value
/// is zero is skipped and holds the index. Days need not be consecutive: each links to
/// the one before it in date order.
/// </remarks>
public static class TimeWeightedReturn
{
    private const decimal Base = 100m;
    private const decimal DaysPerYear = 365m;

    /// <summary>The TWR over <paramref name="days"/>, or <c>null</c> when there are none.</summary>
    public static TwrResult? Compute(IReadOnlyList<ReturnDay> days)
    {
        if (days.Count == 0)
        {
            return null;
        }

        var ordered = days.OrderBy(day => day.Date).ToList();
        for (var i = 1; i < ordered.Count; i++)
        {
            if (ordered[i].Date == ordered[i - 1].Date)
            {
                throw new ArgumentException($"Two days on {ordered[i].Date}; sum them first.", nameof(days));
            }
        }

        var growth = 1m;
        var index = new List<DailyPoint>(ordered.Count) { new(ordered[0].Date, Base) };
        for (var i = 1; i < ordered.Count; i++)
        {
            var previous = ordered[i - 1].Value;
            var day = ordered[i];
            if (previous != 0m)
            {
                // 1 + r_d, taken as one quotient so no rounding creeps in through the "- 1".
                growth *= (day.Value + day.Income - day.Flow) / previous;
            }

            index.Add(new DailyPoint(day.Date, Base * growth));
        }

        var span = ordered[^1].Date.DayNumber - ordered[0].Date.DayNumber;
        return new TwrResult(new Rate(growth - 1m), Annualise(growth, span), span, index);
    }

    /// <summary>
    /// <c>growth^(365 / days) - 1</c>; <c>null</c> when it cannot be represented. Also what a
    /// benchmark's index annualises with, so both sides of the comparison share the guards.
    /// </summary>
    public static Rate? Annualise(decimal growth, int days)
    {
        if (days == 0 || growth < 0m)
        {
            return null;
        }

        if (growth == 0m)
        {
            return new Rate(-1m);
        }

        try
        {
            return new Rate(DecimalMath.Pow(growth, DaysPerYear / days) - 1m);
        }
        catch (OverflowException)
        {
            return null;
        }
    }
}
