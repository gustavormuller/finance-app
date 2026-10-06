using Finance.Api.Domain.Returns;

namespace Finance.Api.Domain.Compare;

/// <summary>One series' result over a comparison: its change from start to end, and that change at a year's rate.</summary>
/// <param name="Annualised"><c>null</c> past the <see cref="decimal"/> range, as <see cref="TimeWeightedReturn.Annualise"/>.</param>
public readonly record struct SeriesChange(Rate Change, Rate? Annualised);

/// <summary>
/// Series side by side: the dates drawn, each series' index on each of them (100 on the
/// first), and each series' change. <c>Indices[s][d]</c> is series <c>s</c> on <c>Dates[d]</c>.
/// </summary>
public sealed record Comparison(
    IReadOnlyList<DateOnly> Dates,
    IReadOnlyList<IReadOnlyList<decimal>> Indices,
    IReadOnlyList<SeriesChange> Changes);

/// <summary>
/// Rebases series to 100 on a common start (026, decisions 7 and 8). Each series is a list of
/// observations in its own unit: closes, a level, or a rate already accumulated by
/// <see cref="RateIndex"/>. Pure; the caller chooses the start and the end.
/// </summary>
/// <remarks>
/// The dates are the start, every observation strictly between the start and the end, and the
/// end: the union of the series' calendars, so a weekday series and an every-day one meet on
/// every day either moved. On each date a series is its last observation on or before it,
/// carried forward, never interpolated.
/// </remarks>
public static class SeriesComparison
{
    private const decimal Base = 100m;

    /// <summary>
    /// Every series needs a positive observation on or before <paramref name="start"/>, and
    /// <paramref name="end"/> must be after it.
    /// </summary>
    public static Comparison Compare(IReadOnlyList<IReadOnlyList<DailyPoint>> series, DateOnly start, DateOnly end)
    {
        if (end <= start)
        {
            throw new ArgumentException($"The comparison ends ({end}) on or before it starts ({start}).", nameof(end));
        }

        var ordered = series.Select(points => points.OrderBy(point => point.Date).ToList()).ToList();
        var dates = ordered.SelectMany(points => points)
            .Select(point => point.Date)
            .Where(date => date > start && date < end)
            .Append(start)
            .Append(end)
            .Distinct()
            .Order()
            .ToList();

        var days = end.DayNumber - start.DayNumber;
        var indices = new List<IReadOnlyList<decimal>>(ordered.Count);
        var changes = new List<SeriesChange>(ordered.Count);
        foreach (var points in ordered)
        {
            var values = CarriedForward(points, dates);
            var first = values[0];
            if (first <= 0m)
            {
                throw new ArgumentException($"A series is {first} on {start}; only a positive value can be rebased.", nameof(series));
            }

            indices.Add(values.Select(value => Base * value / first).ToList());
            var growth = values[^1] / first;
            changes.Add(new SeriesChange(new Rate(growth - 1m), TimeWeightedReturn.Annualise(growth, days)));
        }

        return new Comparison(dates, indices, changes);
    }

    /// <summary>The series' value on each date: its last observation on or before it.</summary>
    private static List<decimal> CarriedForward(List<DailyPoint> points, List<DateOnly> dates)
    {
        var values = new List<decimal>(dates.Count);
        var next = 0;
        decimal? current = null;
        foreach (var date in dates)
        {
            while (next < points.Count && points[next].Date <= date)
            {
                current = points[next].Value;
                next++;
            }

            values.Add(current ?? throw new ArgumentException(
                $"A series has no observation on or before {dates[0]}, so it has no value to rebase.", nameof(points)));
        }

        return values;
    }
}
