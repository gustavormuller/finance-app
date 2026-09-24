using Finance.Api.Domain.MarketData;

namespace Finance.Api.Domain.Investments;

/// <summary>Builds <see cref="PortfolioDaily"/> rows for one asset (007, "SnapshotBuilder").</summary>
public static class SnapshotBuilder
{
    public static IReadOnlyList<PortfolioDaily> Build(
        Guid userId,
        Guid assetId,
        string currency,
        IEnumerable<Movement> movements,
        IEnumerable<Price> prices,
        IEnumerable<Benchmark> fxRates,
        DateOnly from,
        DateOnly to) =>
        throw new NotImplementedException();
}
