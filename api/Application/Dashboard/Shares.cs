namespace Finance.Api.Application.Dashboard;

/// <summary>
/// Each amount's share of their sum, to four decimal places, summing to exactly 1.
/// </summary>
/// <remarks>
/// Rounding each share on its own drifts: three equal thirds round to 0.3333 each and
/// sum to 0.9999, which a chart labelled in percentages shows as a missing sliver.
/// Largest remainder instead: every share is truncated to whole ten-thousandths, and
/// the ten-thousandths left over go one each to the shares that lost the most, ties
/// to the earlier row. Every share is still within 0.0001 of the exact value.
/// </remarks>
internal static class Shares
{
    private const decimal Units = 10_000m;

    private const decimal Unit = 0.0001m;

    public static IReadOnlyList<decimal> Of(IReadOnlyList<decimal> amounts)
    {
        var total = amounts.Sum();

        if (total == 0m)
        {
            return [.. amounts.Select(_ => 0m)];
        }

        var exact = amounts.Select(amount => amount / total * Units).ToArray();
        var units = exact.Select(decimal.Floor).ToArray();

        // All amounts share a sign (rule 3 keeps an Income or Expense category on one
        // side of zero), so what is missing is between zero and one unit per row.
        var missing = (int)Math.Clamp(Units - units.Sum(), 0m, units.Length);

        foreach (var index in Enumerable.Range(0, units.Length)
                     .OrderByDescending(index => exact[index] - units[index])
                     .ThenBy(index => index)
                     .Take(missing))
        {
            units[index] += 1m;
        }

        // Multiplied rather than divided, so the result carries a scale of four and
        // serialises as 0.5000 rather than 0.5.
        return [.. units.Select(share => share * Unit)];
    }
}
