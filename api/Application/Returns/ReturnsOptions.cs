using Finance.Api.Application.MarketData;
using Finance.Api.Domain.Returns;

namespace Finance.Api.Application.Returns;

/// <summary>The <c>Returns</c> configuration section (008): the benchmarks a return is compared with.</summary>
public sealed class ReturnsOptions
{
    public const string Section = "Returns";

    /// <summary>Benchmark key (<c>CDI</c>, <c>IPCA6</c>) to how it accumulates.</summary>
    public Dictionary<string, ReturnsBenchmark> Benchmarks { get; set; } = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// What is wrong with the benchmarks, as English diagnostics for the operator: a series
    /// that <c>MarketData</c> does not configure, a type that does not match the unit 006
    /// records for it (spec test 21), a spread on anything but a monthly rate, or no
    /// benchmark at all. Empty when the configuration is sound.
    /// </summary>
    public static IReadOnlyList<string> Problems(ReturnsOptions returns, MarketDataOptions marketData)
    {
        if (returns.Benchmarks.Count == 0)
        {
            return [$"{Section}:Benchmarks is empty; the returns page compares against these benchmarks."];
        }

        var problems = new List<string>();
        foreach (var (key, benchmark) in returns.Benchmarks.OrderBy(pair => pair.Key, StringComparer.Ordinal))
        {
            var name = $"{Section}:Benchmarks:{key}";
            var source = string.IsNullOrWhiteSpace(benchmark.Source) ? key : benchmark.Source;
            var unit = UnitOf(source, marketData);
            if (unit is null)
            {
                problems.Add($"{name} reads the series {source}, which neither MarketData:Bcb:Series nor "
                    + "MarketData:PriceBenchmarks configures.");
            }
            else if (unit != UnitFor(benchmark.Type))
            {
                problems.Add($"{name} is a {benchmark.Type}, but its series {source} is recorded in MarketData "
                    + $"as {unit}. A {benchmark.Type} reads a {UnitFor(benchmark.Type)} series.");
            }

            if (benchmark.Spread != 0m && benchmark.Type != BenchmarkType.MonthlyRate)
            {
                problems.Add($"{name} has a spread, which applies to a {BenchmarkType.MonthlyRate} only.");
            }
        }

        return problems;
    }

    /// <summary>Fails the boot on any of <see cref="Problems"/>, so a mismatch is never a wrong number on screen.</summary>
    public static void RefuseMismatchedBenchmarks(IConfiguration configuration)
    {
        var returns = configuration.GetSection(Section).Get<ReturnsOptions>() ?? new ReturnsOptions();
        var marketData = configuration.GetSection(MarketDataOptions.Section).Get<MarketDataOptions>() ?? new MarketDataOptions();

        var problems = Problems(returns, marketData);
        if (problems.Count > 0)
        {
            throw new InvalidOperationException(string.Join(" ", problems));
        }
    }

    /// <summary>The unit a type accumulates (008, decision 6, against 006's recorded units).</summary>
    public static BenchmarkUnit UnitFor(BenchmarkType type) => type switch
    {
        BenchmarkType.DailyRate => BenchmarkUnit.PercentPerDay,
        BenchmarkType.MonthlyRate => BenchmarkUnit.PercentPerMonth,
        BenchmarkType.Level => BenchmarkUnit.Level,
        _ => throw new ArgumentOutOfRangeException(nameof(type), type, "Unknown benchmark type."),
    };

    private static BenchmarkUnit? UnitOf(string code, MarketDataOptions marketData) =>
        marketData.Bcb.Series.TryGetValue(code, out var series) ? series.Unit
        : marketData.PriceBenchmarks.TryGetValue(code, out var price) ? price.Unit
        : null;
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
