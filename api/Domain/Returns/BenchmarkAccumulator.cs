namespace Finance.Api.Domain.Returns;

/// <summary>A benchmark's stored values as an index, base 100 at the period start (008, decisions 6 and 7).</summary>
/// <remarks>
/// <paramref name="start"/> is the base day, at exactly 100. A rate row dated after it and
/// on or before the end compounds on its own date; a day without a row holds the index.
/// A level divides by the last value on or before the start and carries forward on gaps.
/// The result has one point per calendar day, <c>start</c> to <c>end</c> inclusive, so it
/// lines up with the portfolio's daily index.
/// </remarks>
public static class BenchmarkAccumulator
{
    private const decimal Base = 100m;

    /// <summary>
    /// The index, or <c>null</c> when the stored values cannot anchor it: a level with no
    /// positive value on or before <paramref name="start"/>, or a rate with no row inside
    /// the period. <paramref name="annualSpread"/> (<c>0.06</c> for 6% a.a.) is added to a
    /// <see cref="BenchmarkType.MonthlyRate"/>, compounded monthly as <c>(1 + s)^(1/12)</c>.
    /// </summary>
    public static IReadOnlyList<DailyPoint>? Accumulate(
        IReadOnlyList<DailyPoint> points, BenchmarkType type, DateOnly start, DateOnly end, Rate annualSpread = default)
    {
        if (end < start)
        {
            throw new ArgumentException($"The period ends ({end}) before it starts ({start}).", nameof(end));
        }

        if (annualSpread.Value != 0m && type != BenchmarkType.MonthlyRate)
        {
            throw new ArgumentException($"A spread applies to a {BenchmarkType.MonthlyRate} only, not a {type}.", nameof(annualSpread));
        }

        var ordered = points.OrderBy(point => point.Date).ToList();
        return type == BenchmarkType.Level
            ? Level(ordered, start, end)
            : Rates(ordered, start, end, MonthlySpreadFactor(annualSpread));
    }

    private static decimal MonthlySpreadFactor(Rate annualSpread) =>
        annualSpread.Value == 0m ? 1m : DecimalMath.Pow(1m + annualSpread.Value, 1m / 12m);

    private static List<DailyPoint>? Rates(List<DailyPoint> ordered, DateOnly start, DateOnly end, decimal spreadFactor)
    {
        var byDate = ordered.Where(point => point.Date > start && point.Date <= end)
            .ToDictionary(point => point.Date, point => point.Value);
        if (byDate.Count == 0)
        {
            return null;
        }

        var index = Base;
        var series = new List<DailyPoint> { new(start, index) };
        for (var day = start.AddDays(1); day <= end; day = day.AddDays(1))
        {
            if (byDate.TryGetValue(day, out var percent))
            {
                index *= (1m + percent / 100m) * spreadFactor;
            }

            series.Add(new DailyPoint(day, index));
        }

        return series;
    }

    private static List<DailyPoint>? Level(List<DailyPoint> ordered, DateOnly start, DateOnly end)
    {
        var anchor = ordered.LastOrDefault(point => point.Date <= start);
        if (anchor == default || anchor.Value <= 0m)
        {
            return null;
        }

        var next = ordered.FindIndex(point => point.Date > start);
        var current = anchor.Value;
        var series = new List<DailyPoint> { new(start, Base) };
        for (var day = start.AddDays(1); day <= end; day = day.AddDays(1))
        {
            while (next >= 0 && next < ordered.Count && ordered[next].Date <= day)
            {
                current = ordered[next].Value;
                next++;
            }

            series.Add(new DailyPoint(day, Base * current / anchor.Value));
        }

        return series;
    }
}
