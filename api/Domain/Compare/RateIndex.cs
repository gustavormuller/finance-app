using Finance.Api.Domain.Returns;

namespace Finance.Api.Domain.Compare;

/// <summary>
/// A rate series (CDI, SELIC, IPCA) as observations of its index, so it compares like a price
/// (026, decision 3). The index is 008's <see cref="BenchmarkAccumulator"/>, not a second
/// accumulation: a row compounds on its own date and a day without one holds the index.
/// </summary>
public static class RateIndex
{
    private const decimal Base = 100m;

    /// <summary>
    /// The index, 100 on <paramref name="start"/>, on the start and on the date of every row in
    /// <c>(start, end]</c>. With no row there it stays 100: the series is flat over the period.
    /// </summary>
    public static IReadOnlyList<DailyPoint> Observations(
        IReadOnlyList<DailyPoint> rows, BenchmarkType type, DateOnly start, DateOnly end)
    {
        if (type == BenchmarkType.Level)
        {
            throw new ArgumentException("A level is already observations of itself; only a rate accumulates.", nameof(type));
        }

        if (BenchmarkAccumulator.Accumulate(rows, type, start, end) is not { } index)
        {
            return [new DailyPoint(start, Base)];
        }

        var rowDates = rows.Select(row => row.Date).Where(date => date > start && date <= end).ToHashSet();
        return [.. index.Where(point => point.Date == start || rowDates.Contains(point.Date))];
    }
}
