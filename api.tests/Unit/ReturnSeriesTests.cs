using Finance.Api.Domain.Investments;
using Finance.Api.Domain.Returns;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// One asset's TWR days from its <see cref="PortfolioDaily"/> rows and movements (008,
/// decision 3 and the CP1 defaults): buys in at cost plus fees, sells out at proceeds less
/// fees, dividends and JCP as income net of fees, USD flows at 007's FX rule, and a flow
/// dated before the first row moved onto it.
/// </summary>
public sealed class ReturnSeriesTests
{
    private static readonly DateOnly Day0 = new(2026, 3, 1);

    [Fact]
    public void A_buy_is_an_inflow_of_cost_plus_fees()
    {
        var days = ReturnSeries.InBrl("BRL", [Row(1, 10m, 10m), Row(2, 20m, 10m)], [Buy(2, 10m, 10m, fees: 1.50m)], []);

        Assert.Equal([new ReturnDay(At(1), 100m, 0m, 0m), new ReturnDay(At(2), 200m, 0m, 101.50m)], days);
    }

    /// <summary>5 sold at 12 with 0.40 in fees: 60 - 0.40 = 59.60 out.</summary>
    [Fact]
    public void A_sell_is_an_outflow_of_proceeds_less_fees()
    {
        var days = ReturnSeries.InBrl("BRL", [Row(1, 10m, 12m), Row(2, 5m, 12m)], [Sell(2, 5m, 12m, fees: 0.40m)], []);

        Assert.Equal(-59.60m, days[1].Flow);
    }

    /// <summary>7 - 0.35 and 3 - 0 on the same day: 9.65 of income, no flow.</summary>
    [Fact]
    public void Dividends_and_jcp_are_income_net_of_fees()
    {
        var days = ReturnSeries.InBrl(
            "BRL", [Row(1, 10m, 10m), Row(2, 10m, 10m)], [Income(2, MovementKind.Dividend, 7m, 0.35m), Income(2, MovementKind.Jcp, 3m, 0m)], []);

        Assert.Equal(new ReturnDay(At(2), 100m, 9.65m, 0m), days[1]);
    }

    [Fact]
    public void A_split_is_neither_a_flow_nor_income()
    {
        var split = new Movement { Date = At(2), Kind = MovementKind.Split, Quantity = 10m, Currency = "BRL" };

        var days = ReturnSeries.InBrl("BRL", [Row(1, 10m, 10m), Row(2, 20m, 5m)], [split], []);

        Assert.Equal(new ReturnDay(At(2), 100m, 0m, 0m), days[1]);
    }

    /// <summary>A buy on day 1 before the first close on day 3 lands on day 3 (DEFERRED, 008 CP1).</summary>
    [Fact]
    public void A_flow_before_the_first_row_moves_onto_it()
    {
        var days = ReturnSeries.InBrl("BRL", [Row(3, 10m, 11m), Row(4, 10m, 12m)], [Buy(1, 10m, 10m)], []);

        Assert.Equal(new ReturnDay(At(3), 110m, 0m, 100m), days[0]);
    }

    [Fact]
    public void A_movement_after_the_last_row_is_left_out()
    {
        var days = ReturnSeries.InBrl("BRL", [Row(1, 10m, 10m)], [Buy(1, 10m, 10m), Buy(2, 5m, 10m)], []);

        Assert.Equal([new ReturnDay(At(1), 100m, 0m, 100m)], days);
    }

    /// <summary>2 at 100 USD plus 1 of fees on day 3, with no rate that day: 201 x 5.10 (day 2) = 1025.10.</summary>
    [Fact]
    public void A_usd_flow_takes_the_latest_rate_on_or_before_its_date()
    {
        var days = ReturnSeries.InBrl(
            "USD", [Row(2, 1m, 100m, 5.10m), Row(3, 3m, 100m, 5.10m)], [Buy(3, 2m, 100m, fees: 1m, "USD")], [Fx(1, 5.00m), Fx(2, 5.10m), Fx(4, 5.20m)]);

        Assert.Equal(new ReturnDay(At(3), 1530m, 0m, 1025.10m), days[1]);
    }

    /// <summary>A buy on day 0, before the first rate on day 1: the earliest rate after it, 5.00.</summary>
    [Fact]
    public void A_usd_flow_before_any_rate_takes_the_earliest_one()
    {
        var days = ReturnSeries.InBrl("USD", [Row(1, 1m, 100m, 5.00m)], [Buy(0, 1m, 100m, currency: "USD")], [Fx(1, 5.00m), Fx(2, 5.10m)]);

        Assert.Equal(500m, days[0].Flow);
    }

    [Fact]
    public void A_usd_flow_without_any_rate_is_refused() =>
        Assert.Throws<InvalidOperationException>(() =>
            ReturnSeries.InBrl("USD", [Row(1, 1m, 100m, 5m)], [Buy(1, 1m, 100m, currency: "USD")], []));

    /// <summary>Native values are quantity x price, unrounded; flows stay in the asset's currency.</summary>
    [Fact]
    public void The_native_series_is_quantity_times_price_with_native_flows()
    {
        var days = ReturnSeries.InNative([Row(1, 1.5m, 100.123m, 5m), Row(2, 3m, 100.123m, 5.1m)], [Buy(2, 1.5m, 100m, fees: 1m, "USD")]);

        Assert.Equal([new ReturnDay(At(1), 150.1845m, 0m, 0m), new ReturnDay(At(2), 300.369m, 0m, 151m)], days);
    }

    private static DateOnly At(int day) => Day0.AddDays(day);

    private static PortfolioDaily Row(int day, decimal quantity, decimal price, decimal fxRate = 1m) => new()
    {
        Date = At(day),
        Quantity = quantity,
        Price = price,
        FxRate = fxRate,
        ValueBrl = Math.Round(quantity * price * fxRate, 2, MidpointRounding.ToEven),
    };

    private static Movement Buy(int day, decimal quantity, decimal unitPrice, decimal fees = 0m, string currency = "BRL") =>
        new() { Date = At(day), Kind = MovementKind.Buy, Quantity = quantity, UnitPrice = unitPrice, Fees = fees, Currency = currency };

    private static Movement Sell(int day, decimal quantity, decimal unitPrice, decimal fees = 0m) =>
        new() { Date = At(day), Kind = MovementKind.Sell, Quantity = quantity, UnitPrice = unitPrice, Fees = fees, Currency = "BRL" };

    private static Movement Income(int day, MovementKind kind, decimal amount, decimal fees) =>
        new() { Date = At(day), Kind = kind, Amount = amount, Fees = fees, Currency = "BRL" };

    private static DailyPoint Fx(int day, decimal rate) => new(At(day), rate);
}
