using Finance.Api.Application.MarketData;
using Finance.Api.Domain.Returns;

namespace Finance.Api.Application.Returns;

/// <summary>The <c>Returns</c> configuration section (008): the benchmarks a return is compared with.</summary>
public sealed class ReturnsOptions
{
    public const string Section = "Returns";

    /// <summary>Benchmark key (<c>CDI</c>, <c>IPCA6</c>) to how it accumulates.</summary>
    public Dictionary<string, ReturnsBenchmark> Benchmarks { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    public static IReadOnlyList<string> Problems(ReturnsOptions returns, MarketDataOptions marketData) =>
        throw new NotImplementedException();

    public static void RefuseMismatchedBenchmarks(IConfiguration configuration) =>
        throw new NotImplementedException();
}

/// <summary>One benchmark: its type, its label, the stored series it reads and an optional spread.</summary>
public sealed class ReturnsBenchmark
{
    public BenchmarkType Type { get; set; }

    public string Label { get; set; } = "";

    /// <summary>The <c>Benchmarks</c> code it reads; the benchmark's own key when empty.</summary>
    public string? Source { get; set; }

    /// <summary>Percent a year added to a monthly rate: <c>6</c> for IPCA + 6%.</summary>
    public decimal Spread { get; set; }
}
