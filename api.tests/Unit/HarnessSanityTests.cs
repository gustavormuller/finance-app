namespace Finance.Api.Tests.Unit;

/// <summary>
/// Proves the unit lane runs without Docker. If these fail, the harness is broken,
/// not the application.
/// </summary>
public sealed class HarnessSanityTests
{
    [Fact]
    public void Unit_lane_runs()
    {
        Assert.Equal(4, 2 + 2);
    }

    [Fact]
    public void Decimal_is_exact_where_double_is_not()
    {
        // Principle 4: money is decimal, never float. This is the reason why.
        Assert.Equal(0.3m, 0.1m + 0.2m);
        Assert.NotEqual(0.3d, 0.1d + 0.2d);
    }
}
