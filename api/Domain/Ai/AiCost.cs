namespace Finance.Api.Domain.Ai;

/// <summary>
/// What one AI call costs in BRL, and which month it counts against. Pure: no clock, no
/// database. Money is <c>decimal</c> throughout (principle 4).
/// </summary>
public static class AiCost
{
    /// <summary>
    /// <c>(input / 1e6) * inputPrice + (output / 1e6) * outputPrice</c>, the prices in USD
    /// per million tokens, converted at <paramref name="usdBrl"/> (009).
    /// </summary>
    public static decimal Brl(int inputTokens, int outputTokens, decimal inputPerMTokUsd, decimal outputPerMTokUsd, decimal usdBrl) =>
        throw new NotImplementedException();

    /// <summary>The latest <c>USDBRL</c> when there is a usable one, otherwise the configured fallback.</summary>
    public static decimal UsdBrl(decimal? latest, decimal fallback) => throw new NotImplementedException();

    /// <summary>The month, <c>YYYY-MM</c>, an instant falls in on a fixed UTC-3 clock.</summary>
    public static string MonthOf(DateTimeOffset instant) => throw new NotImplementedException();
}
