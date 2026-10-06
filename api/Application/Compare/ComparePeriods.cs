namespace Finance.Api.Application.Compare;

/// <summary>The periods <c>GET /api/compare</c> takes (026, decision 5).</summary>
public enum ComparePeriodKind
{
    OneMonth = 0,
    SixMonths = 1,

    /// <summary>From 31 December of the previous year, so the year's first close counts.</summary>
    Ytd = 2,

    OneYear = 3,
    FiveYears = 4,
    TenYears = 5,

    /// <summary>No start of its own: where the latest-starting series begins.</summary>
    Max = 6,

    /// <summary><c>from</c> and <c>to</c>, each optional.</summary>
    Custom = 7,
}

/// <summary>The days a period asks for, both inclusive. <c>From</c> is <c>null</c> when the period has no start of its own.</summary>
public sealed record CompareWindow(DateOnly? From, DateOnly To);

/// <summary>A series' own data: its first day (a rate's is the day before its first row) and its last observation.</summary>
public readonly record struct SeriesCoverage(DateOnly? First, DateOnly? Last);

public enum CompareOutcome
{
    Compared = 0,

    /// <summary>No series covers any part of the window.</summary>
    NoData = 1,

    /// <summary>The series that cover it do not overlap.</summary>
    NoCommonPeriod = 2,
}

/// <summary>
/// What a comparison draws: which series cover the window, and the start (every series 100)
/// and end. <see cref="Start"/> and <see cref="End"/> are meaningful only when compared.
/// </summary>
public sealed record ComparePlan(CompareOutcome Outcome, IReadOnlyList<bool> HasData, DateOnly Start, DateOnly End);

/// <summary>Turns a period into the days a comparison spans (026, decisions 5, 6 and 9). Pure; the caller supplies today.</summary>
public static class ComparePeriods
{
    /// <summary>
    /// A preset counts back from <paramref name="today"/> by the calendar, the 31st going to a
    /// shorter month's last day; custom takes its dates, the end defaulting to today.
    /// </summary>
    public static CompareWindow Window(ComparePeriodKind kind, DateOnly? from, DateOnly? to, DateOnly today) => kind switch
    {
        ComparePeriodKind.OneMonth => new(today.AddMonths(-1), today),
        ComparePeriodKind.SixMonths => new(today.AddMonths(-6), today),
        ComparePeriodKind.Ytd => new(new DateOnly(today.Year - 1, 12, 31), today),
        ComparePeriodKind.OneYear => new(today.AddYears(-1), today),
        ComparePeriodKind.FiveYears => new(today.AddYears(-5), today),
        ComparePeriodKind.TenYears => new(today.AddYears(-10), today),
        ComparePeriodKind.Max => new(null, today),
        ComparePeriodKind.Custom => new(from, to ?? today),
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Unknown period."),
    };

    /// <summary>
    /// A series covers the window when its data spans part of it: <c>max(first, from) &lt; min(last, to)</c>.
    /// Of those, the start is the latest of the window's start and their first days, so every
    /// one has a value there; the end is their latest observation, not after the window's end.
    /// They have no common period when one of them ends on or before the start.
    /// </summary>
    public static ComparePlan Plan(CompareWindow window, IReadOnlyList<SeriesCoverage> coverage)
    {
        var from = window.From ?? DateOnly.MinValue;
        var hasData = coverage
            .Select(series => series is { First: { } first, Last: { } last } && Later(first, from) < Earlier(last, window.To))
            .ToList();
        var covering = coverage.Where((_, position) => hasData[position]).ToList();
        if (covering.Count == 0)
        {
            return new ComparePlan(CompareOutcome.NoData, hasData, default, default);
        }

        var start = Later(from, covering.Max(series => series.First!.Value));
        var end = Earlier(window.To, covering.Max(series => series.Last!.Value));
        var overlap = start < end && covering.All(series => series.Last!.Value > start);
        return new ComparePlan(overlap ? CompareOutcome.Compared : CompareOutcome.NoCommonPeriod, hasData, start, end);
    }

    private static DateOnly Later(DateOnly one, DateOnly other) => one > other ? one : other;

    private static DateOnly Earlier(DateOnly one, DateOnly other) => one < other ? one : other;
}
