using Finance.Api.Application.MarketData;
using Finance.Api.Application.Returns;
using Finance.Api.Domain.Returns;
using Microsoft.Extensions.Configuration;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// 008 spec unit test 21 and the definition of done's "benchmark types in config match the
/// units recorded in 006": each <c>Returns:Benchmarks</c> type is checked against the
/// <see cref="BenchmarkUnit"/> that <c>MarketData</c> records for the series it reads.
/// </summary>
public sealed class ReturnsOptionsTests
{
    [Fact]
    public void Appsettings_binds_the_five_benchmarks_of_the_spec()
    {
        var returns = Bind().GetSection(ReturnsOptions.Section).Get<ReturnsOptions>()!;

        Assert.Equal(
            [("CDI", BenchmarkType.DailyRate, "CDI", null, 0m),
             ("IPCA6", BenchmarkType.MonthlyRate, "IPCA + 6%", "IPCA", 6m),
             ("IVVB11", BenchmarkType.Level, "S&P 500 (IVVB11)", null, 0m),
             ("SELIC", BenchmarkType.DailyRate, "SELIC", null, 0m),
             ("USDBRL", BenchmarkType.Level, "Dólar", null, 0m)],
            returns.Benchmarks.OrderBy(pair => pair.Key, StringComparer.Ordinal).Select(pair =>
                (pair.Key, pair.Value.Type, pair.Value.Label, pair.Value.Source, pair.Value.Spread)));
    }

    [Fact]
    public void Appsettings_types_match_the_units_006_records()
    {
        var configuration = Bind();

        Assert.Empty(ReturnsOptions.Problems(
            configuration.GetSection(ReturnsOptions.Section).Get<ReturnsOptions>()!,
            configuration.GetSection(MarketDataOptions.Section).Get<MarketDataOptions>()!));
    }

    /// <summary>Spec unit test 21: a Level series fed to DailyRate is a config error, not a wrong number.</summary>
    [Fact]
    public void A_level_series_fed_to_daily_rate_is_a_problem()
    {
        var problem = Assert.Single(ReturnsOptions.Problems(With("USDBRL", BenchmarkType.DailyRate), MarketData()));

        Assert.Contains("Returns:Benchmarks:USDBRL", problem);
        Assert.Contains(nameof(BenchmarkType.DailyRate), problem);
        Assert.Contains(nameof(BenchmarkUnit.Level), problem);
    }

    [Theory]
    [InlineData("CDI", BenchmarkType.MonthlyRate)]
    [InlineData("CDI", BenchmarkType.Level)]
    [InlineData("IPCA", BenchmarkType.DailyRate)]
    [InlineData("IVVB11", BenchmarkType.DailyRate)]
    public void Every_other_mismatch_is_a_problem(string code, BenchmarkType type) =>
        Assert.Single(ReturnsOptions.Problems(With(code, type), MarketData()));

    [Theory]
    [InlineData("CDI", BenchmarkType.DailyRate)]
    [InlineData("ipca", BenchmarkType.MonthlyRate)]
    [InlineData("USDBRL", BenchmarkType.Level)]
    [InlineData("IVVB11", BenchmarkType.Level)]
    public void A_matching_type_is_no_problem(string code, BenchmarkType type) =>
        Assert.Empty(ReturnsOptions.Problems(With(code, type), MarketData()));

    [Fact]
    public void The_source_is_read_in_place_of_the_key()
    {
        var derived = With("IPCA6", BenchmarkType.MonthlyRate, source: "IPCA", spread: 6m);

        Assert.Empty(ReturnsOptions.Problems(derived, MarketData()));
    }

    [Fact]
    public void A_series_market_data_does_not_configure_is_a_problem()
    {
        var problem = Assert.Single(ReturnsOptions.Problems(With("IPCA6", BenchmarkType.MonthlyRate), MarketData()));

        Assert.Contains("IPCA6", problem);
    }

    [Fact]
    public void A_spread_on_anything_but_a_monthly_rate_is_a_problem() =>
        Assert.Single(ReturnsOptions.Problems(With("CDI", BenchmarkType.DailyRate, spread: 2m), MarketData()));

    [Fact]
    public void No_benchmark_at_all_is_a_problem() =>
        Assert.Single(ReturnsOptions.Problems(new ReturnsOptions(), MarketData()));

    private static ReturnsOptions With(string key, BenchmarkType type, string? source = null, decimal spread = 0m) =>
        new() { Benchmarks = { [key] = new ReturnsBenchmark { Type = type, Label = key, Source = source, Spread = spread } } };

    private static MarketDataOptions MarketData() => new()
    {
        Bcb =
        {
            Series =
            {
                ["CDI"] = new SgsSeries { Code = 12, Unit = BenchmarkUnit.PercentPerDay },
                ["IPCA"] = new SgsSeries { Code = 433, Unit = BenchmarkUnit.PercentPerMonth },
                ["USDBRL"] = new SgsSeries { Code = 1, Unit = BenchmarkUnit.Level },
            },
        },
        PriceBenchmarks = { ["IVVB11"] = new PriceBenchmark { Symbol = "IVVB11", Unit = BenchmarkUnit.Level } },
    };

    private static IConfiguration Bind() =>
        new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepositoryRoot(), "api", "appsettings.json"), optional: false, reloadOnChange: false)
            .Build();

    private static string RepositoryRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "FinanceApp.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new InvalidOperationException("FinanceApp.slnx not found above the test binaries.");
    }
}
