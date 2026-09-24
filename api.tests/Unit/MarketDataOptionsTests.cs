using Finance.Api.Application.MarketData;
using Microsoft.Extensions.Configuration;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// 006 configuration: the SGS codes and their units are in <c>appsettings.json</c>, beside
/// each other, and bind. The codes themselves still need checking on the SGS portal.
/// </summary>
public sealed class MarketDataOptionsTests
{
    [Fact]
    public void Appsettings_binds_the_sgs_series_with_their_units()
    {
        var options = Bind();

        Assert.Equal("0 3 * * *", options.Schedule);
        Assert.Equal(5, options.BackfillYears);
        Assert.Equal("https://api.bcb.gov.br/dados/serie/bcdata.sgs.", options.Bcb.BaseUrl);
        Assert.Equal(
            [("CDI", 12, BenchmarkUnit.PercentPerDay), ("IPCA", 433, BenchmarkUnit.PercentPerMonth),
             ("SELIC", 11, BenchmarkUnit.PercentPerDay), ("USDBRL", 1, BenchmarkUnit.Level)],
            options.Bcb.Series.OrderBy(pair => pair.Key, StringComparer.Ordinal)
                .Select(pair => (pair.Key, pair.Value.Code, pair.Value.Unit)));
    }

    [Fact]
    public void Series_codes_are_looked_up_ignoring_case()
    {
        Assert.Equal(12, Bind().Bcb.Series["cdi"].Code);
    }

    [Fact]
    public void Appsettings_commits_no_provider_key()
    {
        var options = Bind();

        Assert.Equal("", options.Brapi.Token);
        Assert.Equal("", options.CoinGecko.DemoKey);
        Assert.Equal("", options.TwelveData.Key);
    }

    private static MarketDataOptions Bind()
    {
        var configuration = new ConfigurationBuilder()
            .AddJsonFile(Path.Combine(RepositoryRoot(), "api", "appsettings.json"), optional: false, reloadOnChange: false)
            .Build();

        return configuration.GetSection(MarketDataOptions.Section).Get<MarketDataOptions>()
            ?? throw new InvalidOperationException("No MarketData section in appsettings.json.");
    }

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
