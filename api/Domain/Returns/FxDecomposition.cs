using Finance.Api.Domain.Investments;

namespace Finance.Api.Domain.Returns;

/// <summary>A non-BRL asset's return split into the asset's own and the exchange rate's.</summary>
/// <param name="Native">TWR in the asset's currency.</param>
/// <param name="Fx"><c>(1 + Total) / (1 + Native) - 1</c>.</param>
/// <param name="Total">TWR in BRL.</param>
public sealed record FxSplit(Rate Native, Rate Fx, Rate Total);

/// <summary>
/// 008's <c>FxDecomposition</c> (decision 8): <c>(1 + r_total) = (1 + r_native)(1 + r_fx)</c>.
/// <c>r_native</c> is TWR on <c>Quantity x Price</c> with native flows, <c>r_total</c> TWR
/// on <c>ValueBrl</c> with flows at 007's FX rule, and <c>r_fx</c> is what is left. The
/// identity holds by construction.
/// </summary>
public static class FxDecomposition
{
    /// <summary>
    /// The split, or <c>null</c> for a BRL asset or one with no rows. When the native return
    /// is -100%, both sides of the identity are zero whatever <c>r_fx</c> is; it is reported
    /// as zero.
    /// </summary>
    public static FxSplit? Split(
        string currency, IReadOnlyList<PortfolioDaily> rows, IEnumerable<Movement> movements, IReadOnlyList<DailyPoint> fxRates)
    {
        if (currency == SnapshotBuilder.BaseCurrency)
        {
            return null;
        }

        var history = movements.ToList();
        var native = TimeWeightedReturn.Compute(ReturnSeries.InNative(rows, history));
        var total = TimeWeightedReturn.Compute(ReturnSeries.InBrl(currency, rows, history, fxRates));
        if (native is null || total is null)
        {
            return null;
        }

        var nativeGrowth = 1m + native.Total.Value;
        var fx = nativeGrowth == 0m ? 0m : (1m + total.Total.Value) / nativeGrowth - 1m;
        return new FxSplit(native.Total, new Rate(fx), total.Total);
    }
}
