using Finance.Api.Domain.Ai;

namespace Finance.Api.Tests.Unit;

/// <summary>Money is <c>decimal</c>, AI costs included (principle 4).</summary>
public sealed class AiTypesTests
{
    private static readonly string[] Namespaces =
    [
        "Finance.Api.Domain.Ai",
        "Finance.Api.Application.Ai",
        "Finance.Api.Infrastructure.Ai",
    ];

    [Fact]
    public void No_ai_type_declares_a_double_or_a_float()
    {
        var types = typeof(AiUsage).Assembly.GetTypes()
            .Where(type => Namespaces.Contains(type.Namespace))
            .ToList();

        Assert.Contains(typeof(AiUsage), types);
        Assert.Contains(typeof(AiAnalysis), types);

        Assert.Empty(FloatingPointMembers.In(types));
    }

    [Fact]
    public void The_cost_of_a_call_is_a_decimal()
    {
        Assert.Equal(typeof(decimal), typeof(AiUsage).GetProperty(nameof(AiUsage.CostBrl))!.PropertyType);
    }
}
