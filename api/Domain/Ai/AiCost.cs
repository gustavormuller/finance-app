namespace Finance.Api.Domain.Ai;

/// <summary>
/// What one AI call costs in BRL, and which month it counts against. Pure: no clock, no
/// database. Money is <c>decimal</c> throughout (principle 4).
/// </summary>
public static class AiCost
{
    /// <summary>
    /// <c>(input / 1e6) * inputPrice + (output / 1e6) * outputPrice</c>, the prices in USD
    /// per million tokens, converted at <paramref name="usdBrl"/>.
    /// </summary>
    /// <remarks>
    /// Computed in full and rounded once, to the column's four places, half away from zero as
    /// PostgreSQL's <c>numeric</c> rounds: what is summed is what was computed, and a call of
    /// a few tokens never costs zero.
    /// </remarks>
    public static decimal Brl(int inputTokens, int outputTokens, decimal inputPerMTokUsd, decimal outputPerMTokUsd, decimal usdBrl)
    {
        var usdMillionths = inputTokens * inputPerMTokUsd + outputTokens * outputPerMTokUsd;
        // Adding a zero of scale 4 fixes the scale at 4, so 50 reads 50.0000 like the column.
        return Math.Round(usdMillionths * usdBrl / TokensPerPrice, CostPlaces, MidpointRounding.AwayFromZero) + 0.0000m;
    }

    /// <summary>The latest <c>USDBRL</c> when there is a usable one, otherwise the configured fallback.</summary>
    public static decimal UsdBrl(decimal? latest, decimal fallback) => latest is > 0m ? latest.Value : fallback;

    /// <summary>The month, <c>YYYY-MM</c>, an instant falls in on a fixed UTC-3 clock.</summary>
    /// <remarks>
    /// Brazil has kept no daylight saving time since 2019, the same fixed offset the brapi
    /// adapter uses. A call at 23:30 on the 31st counts against that month.
    /// </remarks>
    public static string MonthOf(DateTimeOffset instant) =>
        instant.ToOffset(SaoPaulo).ToString("yyyy-MM", System.Globalization.CultureInfo.InvariantCulture);

    private const decimal TokensPerPrice = 1_000_000m;

    private const int CostPlaces = 4;

    private static readonly TimeSpan SaoPaulo = TimeSpan.FromHours(-3);
}
