using Finance.Api.Domain.Investments;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// The declared-type half of 007 spec test 9: every quantity, price, FX rate and
/// amount in investments is <c>decimal</c> (principle 4).
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
        var types = typeof(Movement).Assembly.GetTypes()
            .Where(type => Namespaces.Contains(type.Namespace))
            .ToList();

        Assert.Contains(typeof(Movement), types);
        Assert.Contains(typeof(PortfolioDaily), types);

        Assert.Empty(FloatingPointMembers.In(types));
    }
}
