namespace Finance.Api.Application.Compare;

/// <summary>Which dates of a comparison the chart gets (026, decision 12), so a long range stays light.</summary>
public static class ComparisonSampling
{
    /// <summary>Three years: every date up to here.</summary>
    public const int EveryDateUpToDays = 1096;

    /// <summary>Ten years: one date a week up to here, one a month beyond.</summary>
    public const int WeeklyUpToDays = 3653;

    /// <summary>
    /// Positions to keep in <paramref name="dates"/> (ascending, from the start to the end):
    /// all of them up to three years; past that, the last date of each ISO week, or past ten
    /// years of each month. The first and the last are always kept.
    /// </summary>
    public static IReadOnlyList<int> Keep(IReadOnlyList<DateOnly> dates)
    {
        if (dates.Count == 0)
        {
            return [];
        }

        var span = dates[^1].DayNumber - dates[0].DayNumber;
        if (span <= EveryDateUpToDays)
        {
            return [.. Enumerable.Range(0, dates.Count)];
        }

        Func<DateOnly, int> bucket = span <= WeeklyUpToDays ? MondayOf : date => date.Year * 12 + date.Month;
        var kept = new List<int> { 0 };
        for (var position = 1; position < dates.Count; position++)
        {
            if (position == dates.Count - 1 || bucket(dates[position + 1]) != bucket(dates[position]))
            {
                kept.Add(position);
            }
        }

        return kept;
    }

    /// <summary>The day number of the Monday that opens the date's ISO week.</summary>
    private static int MondayOf(DateOnly date) => date.DayNumber - ((int)date.DayOfWeek + 6) % 7;
}
