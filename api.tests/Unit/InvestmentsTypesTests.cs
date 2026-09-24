using System.Reflection;
using Finance.Api.Domain.Investments;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// The declared-type half of 007 spec test 9: every quantity, price, FX rate and
/// amount in investments is <c>decimal</c> (principle 4). The namespaces 007's later
/// checkpoints add are in the scan already, so a <c>double</c> fails when written.
/// </summary>
public sealed class InvestmentsTypesTests
{
    private static readonly string[] Namespaces =
    [
        "Finance.Api.Domain.Investments",
        "Finance.Api.Application.Investments",
    ];

    [Fact]
    public void No_investments_type_declares_a_double_or_a_float()
    {
        const BindingFlags Declared =
            BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static;

        var types = typeof(Movement).Assembly.GetTypes()
            .Where(type => Namespaces.Contains(type.Namespace))
            .ToList();

        Assert.Contains(typeof(Movement), types);
        Assert.Contains(typeof(PortfolioDaily), types);

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
