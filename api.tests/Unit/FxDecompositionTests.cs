using Finance.Api.Domain.Investments;
using Finance.Api.Domain.Returns;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// 008 spec unit tests 22-24: <c>(1 + total) = (1 + native)(1 + fx)</c>, native being TWR
/// on quantity x price with native flows and total TWR on <c>ValueBrl</c> with BRL flows.
/// </summary>
public sealed class FxDecompositionTests
{
    private static readonly DateOnly Day0 = new(2026, 3, 2);

    /// <summary>Spec unit test 22: 10 units, 10 to 11 USD, 5.00 to 5.50 BRL. 500 BRL to 605: +21%.</summary>
    [Fact]
    public void Ten_percent_native_and_ten_percent_fx_is_twenty_one_percent()
    {
        var split = Split([Row(0, 10m, 10m, 5.00m), Row(1, 10m, 11m, 5.50m)], [Buy(0, 10m, 10m)], [Fx(0, 5.00m), Fx(1, 5.50m)]);

        Assert.Equal(new FxSplit(new Rate(0.10m), new Rate(0.10m), new Rate(0.21m)), split);
        AssertIdentity(split);
    }

    /// <summary>Spec unit test 23: 5.00 to 4.50 BRL. 500 BRL to 495: -1%.</summary>
    [Fact]
    public void Ten_percent_native_and_minus_ten_percent_fx_is_minus_one_percent()
    {
        var split = Split([Row(0, 10m, 10m, 5.00m), Row(1, 10m, 11m, 4.50m)], [Buy(0, 10m, 10m)], [Fx(0, 5.00m), Fx(1, 4.50m)]);

        Assert.Equal(new FxSplit(new Rate(0.10m), new Rate(-0.10m), new Rate(-0.01m)), split);
        AssertIdentity(split);
    }

    /// <summary>Spec unit test 24.</summary>
    [Fact]
    public void A_brl_asset_has_no_fx_split() =>
        Assert.Null(FxDecomposition.Split("BRL", [Row(0, 10m, 10m, 1m), Row(1, 10m, 11m, 1m)], [Buy(0, 10m, 10m)], []));

    /// <summary>Rates and prices that do not divide evenly, a buy with fees mid-period: the identity still holds.</summary>
    [Fact]
    public void The_identity_holds_with_a_flow_at_its_own_rate()
    {
        var split = Split(
            [Row(0, 10m, 10m, 5.00m), Row(1, 13m, 10.7m, 5.13m), Row(2, 13m, 10.2m, 5.31m), Row(3, 13m, 10.93m, 4.97m)],
            [Buy(0, 10m, 10m), Buy(1, 3m, 10.7m, fees: 1m)],
            [Fx(0, 5.00m), Fx(1, 5.13m), Fx(2, 5.31m), Fx(3, 4.97m)]);

        Assert.NotEqual(0m, split.Fx.Value);
        AssertIdentity(split);
    }

    /// <summary>Both sides are zero whatever fx is; it is reported as zero rather than divided by zero.</summary>
    [Fact]
    public void A_total_native_loss_reports_no_fx_return()
    {
        var split = Split([Row(0, 10m, 10m, 5m), Row(1, 10m, 0m, 5.5m)], [Buy(0, 10m, 10m)], [Fx(0, 5m), Fx(1, 5.5m)]);

        Assert.Equal(new FxSplit(new Rate(-1m), new Rate(0m), new Rate(-1m)), split);
    }

    private static FxSplit Split(PortfolioDaily[] rows, Movement[] movements, DailyPoint[] rates) =>
        FxDecomposition.Split("USD", rows, movements, rates) ?? throw new Xunit.Sdk.XunitException("Expected a split, got null.");

    private static void AssertIdentity(FxSplit split)
    {
        var gap = (1m + split.Total.Value) - (1m + split.Native.Value) * (1m + split.Fx.Value);
        Assert.True(Math.Abs(gap) <= 1e-10m, $"(1 + total) - (1 + native)(1 + fx) = {gap}");
    }

    private static PortfolioDaily Row(int day, decimal quantity, decimal price, decimal fxRate) => new()
    {
        Date = Day0.AddDays(day),
        Quantity = quantity,
        Price = price,
        FxRate = fxRate,
        ValueBrl = Math.Round(quantity * price * fxRate, 2, MidpointRounding.ToEven),
    };

    private static Movement Buy(int day, decimal quantity, decimal unitPrice, decimal fees = 0m) => new()
    {
        Date = Day0.AddDays(day), Kind = MovementKind.Buy, Quantity = quantity, UnitPrice = unitPrice, Fees = fees, Currency = "USD",
    };

    private static DailyPoint Fx(int day, decimal rate) => new(Day0.AddDays(day), rate);
}
