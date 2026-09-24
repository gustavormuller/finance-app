using System.Reflection;
using Finance.Api.Application.MarketData;
using Finance.Api.Domain.MarketData;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// The declared-type half of 006 spec test 9: every value from a provider reaches the
/// database as <c>decimal</c>. Checkpoint 1 covers the entities and the points the
/// ports return; the parsers under <c>Infrastructure.MarketData</c> are in the same
/// scan, so a provider DTO with a <c>double</c> fails here when it is written.
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
        const BindingFlags Declared =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        var types = typeof(Price).Assembly.GetTypes()
            .Where(type => Namespaces.Contains(type.Namespace))
            .ToList();

        Assert.Contains(typeof(Price), types);

        var offenders = types
            .SelectMany(type => type.GetProperties(Declared).Select(member => (type, member.Name, member.PropertyType))
                .Concat(type.GetFields(Declared).Select(member => (type, member.Name, member.FieldType))))
            .Where(member => IsBinaryFloatingPoint(member.Item3))
            .Select(member => $"{member.type.FullName}.{member.Name}")
            .ToList();

        Assert.Empty(offenders);
    }

    private static bool IsBinaryFloatingPoint(Type type)
    {
        var underlying = Nullable.GetUnderlyingType(type) ?? type;
        if (underlying.IsArray)
        {
            underlying = underlying.GetElementType()!;
        }

        return underlying == typeof(double) || underlying == typeof(float) || underlying == typeof(Half);
    }
}
