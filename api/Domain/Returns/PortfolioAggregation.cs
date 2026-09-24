namespace Finance.Api.Domain.Returns;

/// <summary>
/// The portfolio as one position (008, "Portfolio aggregation"): value, income and flow
/// summed per day across the assets, in BRL. An asset with no day on a date, because it
/// has not started yet, contributes zero to it.
/// </summary>
/// <remarks>
/// Its TWR is then the value-weighted chaining of the assets, not an average of their
/// TWRs. Build each asset's days with <see cref="ReturnSeries.InBrl"/>, so a buy made
/// before the asset's first close sits on the day its value first appears.
/// </remarks>
public static class PortfolioAggregation
{
    /// <summary>One day per date any asset has, in date order.</summary>
    public static IReadOnlyList<ReturnDay> Sum(IEnumerable<IReadOnlyList<ReturnDay>> assets) =>
        assets
            .SelectMany(days => days)
            .GroupBy(day => day.Date)
            .OrderBy(group => group.Key)
            .Select(group => new ReturnDay(
                group.Key, group.Sum(day => day.Value), group.Sum(day => day.Income), group.Sum(day => day.Flow)))
            .ToList();
}
