using Finance.Api.Domain.Investments;
using Finance.Api.Domain.Returns;
using Finance.Api.Domain.Transactions;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// XIRR's flows for one asset (008, decision 4, and the CP1/CP2 defaults), from the investor's
/// side: buys out at cost plus fees, sells in at proceeds less fees, dividends and JCP in net
/// of fees, the opening value out and the closing value in. Each movement keeps its real date,
/// and a USD one converts at 007's rule for that date.
/// </summary>
public sealed class XirrFlowsTests
{
    private static readonly DateOnly Day0 = new(2026, 3, 1);

    [Fact]
    public void Movements_are_cash_from_the_investors_side()
    {
        Movement[] movements =
        [
            Buy(1, 10m, 10m, fees: 1.50m),
            Sell(2, 5m, 12m, fees: 0.40m),
            Income(3, MovementKind.Dividend, 7m, 0.35m),
            Income(3, MovementKind.Jcp, 3m, 0m),
            new() { Date = At(4), Kind = MovementKind.Split, Quantity = 10m, Currency = "BRL" },
        ];

        var flows = Flows("BRL", movements, [], Point(0, 0m), Point(5, 0m));

        Assert.Equal([Brl(1, -101.50m), Brl(2, 59.60m), Brl(3, 6.65m), Brl(3, 3m)], flows);
    }

    /// <summary>A period that does not start at inception: the opening value goes in, as if bought that day.</summary>
    [Fact]
    public void The_opening_value_is_an_outflow_and_the_closing_value_an_inflow()
    {
        var flows = Flows("BRL", [], [], Point(0, 1000m), Point(10, 1100m));

        Assert.Equal([Brl(0, -1000m), Brl(10, 1100m)], flows);
    }

    /// <summary>
    /// At inception the base day is worth nothing, so there is no opening flow. A movement
    /// on or before the base day is in its value already; one after the closing day is outside.
    /// </summary>
    [Fact]
    public void Only_movements_after_the_base_day_and_up_to_the_closing_day_count()
    {
        var flows = Flows("BRL", [Buy(0, 1m, 50m), Buy(1, 1m, 100m), Buy(11, 1m, 70m)], [], Point(0, 0m), Point(10, 120m));

        Assert.Equal([Brl(1, -100m), Brl(10, 120m)], flows);
    }

    /// <summary>
    /// TWR moves a buy made before the asset's first close onto that close (DEFERRED, 008 CP1).
    /// XIRR has no daily value to match, so the buy keeps its own date.
    /// </summary>
    [Fact]
    public void A_buy_keeps_its_real_date()
    {
        var flows = Flows("BRL", [Buy(2, 1m, 100m)], [], Point(0, 0m), Point(10, 100m));

        Assert.Equal(At(2), flows[0].Date);
    }

    /// <summary>
    /// 2 at 100 USD plus 1 of fees on day 3, no rate that day: 201 x 5.10 (day 2) = 1025.10 out.
    /// A dividend of 3 USD on day 5: 3 x 5.20 (day 4) = 15.60 in. Values are BRL already.
    /// </summary>
    [Fact]
    public void A_usd_movement_converts_at_the_rate_for_its_own_date()
    {
        var flows = Flows(
            "USD",
            [Buy(3, 2m, 100m, fees: 1m, "USD"), Income(5, MovementKind.Dividend, 3m, 0m, "USD")],
            [Fx(1, 5.00m), Fx(4, 5.20m), Fx(2, 5.10m)],
            Point(0, 0m),
            Point(6, 1050m));

        Assert.Equal([Brl(3, -1025.10m), Brl(5, 15.60m), Brl(6, 1050m)], flows);
    }

    /// <summary>1000 in the day after the base day, worth 1100 a year later: 10%.</summary>
    [Fact]
    public void The_flows_feed_xirr()
    {
        var flows = Flows("BRL", [Buy(1, 10m, 100m)], [], Point(0, 0m), Point(366, 1100m));

        var xirr = MoneyWeightedReturn.Compute(flows)!.Value.Value;

        Assert.True(Math.Abs(xirr - 0.10m) <= 1e-8m, $"expected 0.10, got {xirr}");
    }

    [Fact]
    public void A_closing_day_before_the_base_day_is_refused() =>
        Assert.Throws<ArgumentException>(() => Flows("BRL", [], [], Point(5, 0m), Point(4, 0m)));

    private static IReadOnlyList<CashFlow> Flows(
        string currency, Movement[] movements, DailyPoint[] fxRates, DailyPoint opening, DailyPoint closing) =>
        MoneyWeightedReturn.FlowsInBrl(currency, movements, fxRates, opening, closing);

    private static DateOnly At(int day) => Day0.AddDays(day);

    private static DailyPoint Point(int day, decimal valueBrl) => new(At(day), valueBrl);

    private static CashFlow Brl(int day, decimal amount) => new(At(day), new Money(amount, "BRL"));

    private static Movement Buy(int day, decimal quantity, decimal unitPrice, decimal fees = 0m, string currency = "BRL") =>
        new() { Date = At(day), Kind = MovementKind.Buy, Quantity = quantity, UnitPrice = unitPrice, Fees = fees, Currency = currency };

    private static Movement Sell(int day, decimal quantity, decimal unitPrice, decimal fees = 0m) =>
        new() { Date = At(day), Kind = MovementKind.Sell, Quantity = quantity, UnitPrice = unitPrice, Fees = fees, Currency = "BRL" };

    private static Movement Income(int day, MovementKind kind, decimal amount, decimal fees, string currency = "BRL") =>
        new() { Date = At(day), Kind = kind, Amount = amount, Fees = fees, Currency = currency };

    private static DailyPoint Fx(int day, decimal rate) => new(At(day), rate);
}
