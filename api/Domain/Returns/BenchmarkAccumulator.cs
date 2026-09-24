namespace Finance.Api.Domain.Returns;

/// <summary>A benchmark's stored values as an index, base 100 at the period start (008, decisions 6 and 7).</summary>
public static class BenchmarkAccumulator
{
    public static IReadOnlyList<DailyPoint>? Accumulate(
        IReadOnlyList<DailyPoint> points, BenchmarkType type, DateOnly start, DateOnly end, Rate annualSpread = default) =>
        throw new NotImplementedException();
}
