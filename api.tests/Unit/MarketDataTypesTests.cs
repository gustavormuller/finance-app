using Finance.Api.Application.MarketData;
using Finance.Api.Domain.MarketData;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// The declared-type half of 006 spec test 9: every value from a provider reaches the
/// database as <c>decimal</c>. The scan covers the entities, the points the ports return
/// and the parsers under <c>Infrastructure.MarketData</c>, so a provider DTO with a
/// <c>double</c> fails here.
/// </summary>
public sealed class MarketDataTypesTests
{
    private static readonly string[] Namespaces =
    [
        "Finance.Api.Domain.MarketData",
        "Finance.Api.Application.MarketData",
        "Finance.Api.Infrastructure.MarketData",
    ];

    [Fact]
    public void Prices_and_benchmark_values_are_declared_decimal()
    {
        Assert.Equal(typeof(decimal), typeof(Price).GetProperty(nameof(Price.Close))!.PropertyType);
        Assert.Equal(typeof(decimal), typeof(Benchmark).GetProperty(nameof(Benchmark.Value))!.PropertyType);
        Assert.Equal(typeof(decimal), typeof(DailyClose).GetProperty(nameof(DailyClose.Close))!.PropertyType);
        Assert.Equal(typeof(decimal), typeof(DailyValue).GetProperty(nameof(DailyValue.Value))!.PropertyType);
    }

    [Fact]
    public void No_market_data_type_declares_a_double_or_a_float()
    {
        var types = typeof(Price).Assembly.GetTypes()
            .Where(type => Namespaces.Contains(type.Namespace))
            .ToList();

        Assert.Contains(typeof(Price), types);

        Assert.Empty(FloatingPointMembers.In(types));
    }
}
