namespace Finance.Api.Application.Returns;

/// <summary>The periods <c>GET /api/returns</c> takes (008, decision 9).</summary>
public enum ReturnsPeriodKind
{
    /// <summary>From the first movement. The default.</summary>
    Inception = 0,

    /// <summary>From the first of January of today's year.</summary>
    Ytd = 1,

    /// <summary>From the day after the same date a year ago, so the base day is a year before today.</summary>
    TwelveMonths = 2,

    /// <summary><c>from</c> and <c>to</c>, each optional.</summary>
    Custom = 3,
}

/// <summary>The first and last day a return covers, both inclusive. The base day is <c>From - 1</c>.</summary>
public sealed record PeriodRange(DateOnly From, DateOnly To);

/// <summary>Turns a period and its dates into the days a return is measured over. Pure; the caller supplies today.</summary>
public static class ReturnsPeriods
{
    /// <summary>
    /// The range, or <c>null</c> when nothing was held in it. A start before
    /// <paramref name="firstMovement"/> is clamped to it (spec test 29). The end is
    /// clamped to <paramref name="today"/> and to <paramref name="lastRow"/>, the last day
    /// with a value, so the closing value is always a real one.
    /// </summary>
    public static PeriodRange? Resolve(
        ReturnsPeriodKind kind, DateOnly? from, DateOnly? to, DateOnly today, DateOnly firstMovement, DateOnly lastRow)
    {
        var start = kind switch
        {
            ReturnsPeriodKind.Ytd => new DateOnly(today.Year, 1, 1),
            ReturnsPeriodKind.TwelveMonths => today.AddYears(-1).AddDays(1),
            ReturnsPeriodKind.Custom => from ?? firstMovement,
            _ => firstMovement,
        };
        var end = kind == ReturnsPeriodKind.Custom ? to ?? today : today;

        start = start < firstMovement ? firstMovement : start;
        end = new[] { end, today, lastRow }.Min();
        return start > end ? null : new PeriodRange(start, end);
    }
}

/// <summary>Which days of a daily series the chart gets (008: "at most 260 points, weekly over 5 years").</summary>
public static class SeriesSampling
{
    public const int MaximumPoints = 260;

    /// <summary>
    /// Positions to keep out of <paramref name="count"/>: all of them when they fit,
    /// otherwise every <c>s</c>-th from the first, with <c>s = ceil((count - 1) / (maximum - 1))</c>
    /// whole days, and always the last. Weekly up to 1 814 days; coarser past that.
    /// </summary>
    public static IReadOnlyList<int> Positions(int count, int maximum)
    {
        if (count <= maximum)
        {
            return Enumerable.Range(0, count).ToList();
        }

        var last = count - 1;
        var stride = (last + maximum - 2) / (maximum - 1);
        var positions = new List<int>();
        for (var position = 0; position < last; position += stride)
        {
            positions.Add(position);
        }

        positions.Add(last);
        return positions;
    }
}
