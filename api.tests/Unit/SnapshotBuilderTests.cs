using Finance.Api.Domain.Investments;
using Finance.Api.Domain.MarketData;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// 007 spec unit tests 10-15: one row per calendar day, carried prices, and the BRL
/// cost basis at each buy's own FX rate. The range is Monday 2026-09-07 to Sunday
/// 2026-09-20: 14 calendar days, 10 trading days.
/// </summary>
public sealed class SnapshotBuilderTests
{
    private static readonly Guid UserId = Guid.NewGuid();

    private static readonly Guid AssetId = Guid.NewGuid();

    private static readonly DateOnly Monday = new(2026, 9, 7);

    private static readonly DateOnly LastSunday = Monday.AddDays(13);

    /// <summary>Spec unit test 10.</summary>
    [Fact]
    public void Every_calendar_day_has_a_row_and_weekends_carry_friday()
    {
        var rows = Brl([Buy(Monday, 100m, 10m)], WeekdayCloses());

        Assert.Equal(14, rows.Count);
        Assert.Equal(Enumerable.Range(0, 14).Select(Monday.AddDays), rows.Select(r => r.Date));

        var friday = Monday.AddDays(4);
        foreach (var weekend in rows.Where(r => r.Date is { DayOfWeek: DayOfWeek.Saturday } or { DayOfWeek: DayOfWeek.Sunday } && r.Date < Monday.AddDays(7)))
        {
            Assert.Equal(friday, weekend.PriceDate);
            Assert.Equal(Close(friday), weekend.Price);
        }

        Assert.All(rows, r => Assert.Equal(1m, r.FxRate));
        Assert.All(rows, r => Assert.Equal((UserId, AssetId), (r.UserId, r.AssetId)));
    }

    /// <summary>Spec unit test 11.</summary>
    [Fact]
    public void Rows_start_at_the_first_movement()
    {
        var rows = Brl([Buy(Monday.AddDays(4), 100m, 10m)], WeekdayCloses());

        Assert.Equal(10, rows.Count);
        Assert.Equal(Monday.AddDays(4), rows[0].Date);
    }

    /// <summary>Spec unit test 12.</summary>
    [Fact]
    public void No_price_history_means_no_rows() =>
        Assert.Empty(Brl([Buy(Monday, 100m, 10m)], []));

    /// <summary>Decision/test 12, partially: days before the first close are skipped.</summary>
    [Fact]
    public void Days_before_the_first_close_are_skipped()
    {
        var rows = Brl([Buy(Monday, 100m, 10m)], [new Price { Date = Monday.AddDays(2), Close = 11m }]);

        Assert.Equal(Monday.AddDays(2), rows[0].Date);
        Assert.Equal(1100m, rows[0].ValueBrl);
    }

    [Fact]
    public void Building_from_a_date_counts_earlier_movements()
    {
        var rows = SnapshotBuilder.Build(
            UserId, AssetId, "BRL", [Buy(Monday, 100m, 10m)], WeekdayCloses(), [], Monday.AddDays(7), LastSunday);

        Assert.Equal(7, rows.Count);
        Assert.Equal(100m, rows[0].Quantity);
    }

    /// <summary>Spec unit test 13. Bought at 5.00 BRL/USD, valued at 5.20.</summary>
    [Fact]
    public void Usd_asset_is_valued_at_the_day_rate_and_cost_at_the_purchase_rate()
    {
        var rows = Usd(
            [Buy(Monday, 10m, 100m)],
            [new Price { Date = Monday, Close = 100m }, new Price { Date = Monday.AddDays(1), Close = 110m }],
            [Fx(Monday, 5m), Fx(Monday.AddDays(1), 5.2m)]);

        var tuesday = rows.Single(r => r.Date == Monday.AddDays(1));
        Assert.Equal(5.2m, tuesday.FxRate);
        Assert.Equal(5720m, tuesday.ValueBrl);
        Assert.Equal(5000m, tuesday.CostBasisBrl);
    }

    /// <summary>
    /// Spec unit test 14. 1000 USD at 5 plus 1000 USD at 6 cost 11000 BRL, not
    /// 20 x 100 x 7 = 14000 at today's rate.
    /// </summary>
    [Fact]
    public void Two_buys_at_different_rates_sum_their_own_costs()
    {
        var rows = Usd(
            [Buy(Monday, 10m, 100m), Buy(Monday.AddDays(1), 10m, 100m)],
            [new Price { Date = Monday, Close = 100m }],
            [Fx(Monday, 5m), Fx(Monday.AddDays(1), 6m), Fx(Monday.AddDays(2), 7m)]);

        Assert.Equal(11000m, rows[^1].CostBasisBrl);
        Assert.Equal(14000m, rows[^1].ValueBrl);
    }

    /// <summary>Spec unit test 15. Selling a quarter of the units removes a quarter of the cost.</summary>
    [Fact]
    public void Sell_reduces_the_cost_basis_proportionally()
    {
        var rows = Usd(
            [Buy(Monday, 10m, 100m), Buy(Monday.AddDays(1), 10m, 100m), Sell(Monday.AddDays(2), 5m, 120m)],
            [new Price { Date = Monday, Close = 100m }],
            [Fx(Monday, 5m), Fx(Monday.AddDays(1), 6m)]);

        Assert.Equal(11000m, rows.Single(r => r.Date == Monday.AddDays(1)).CostBasisBrl);
        Assert.Equal(8250m, rows[^1].CostBasisBrl);
        Assert.Equal(15m, rows[^1].Quantity);
    }

    /// <summary>
    /// Not in the spec: a USD buy dated before the first known rate costs at the
    /// earliest rate after it, and days with no rate yet are skipped.
    /// </summary>
    [Fact]
    public void Buy_before_the_first_rate_costs_at_the_earliest_later_rate()
    {
        var rows = Usd(
            [Buy(Monday, 10m, 100m)],
            [new Price { Date = Monday, Close = 100m }],
            [Fx(Monday.AddDays(2), 5m)]);

        Assert.Equal(Monday.AddDays(2), rows[0].Date);
        Assert.Equal(5000m, rows[0].CostBasisBrl);
    }

    /// <summary>
    /// Rounding at the column: average cost to 8 places, BRL amounts to 2, both half to
    /// even, as <c>Money</c> does. 30.01 / 3 = 10.00333...; 3 x 0.125 = 0.375 -> 0.38;
    /// 1 x 0.125 = 0.125 -> 0.12.
    /// </summary>
    [Fact]
    public void Values_are_rounded_only_where_written()
    {
        var rows = Brl(
            [Buy(Monday, 3m, 10m, fees: 0.01m), Sell(Monday.AddDays(1), 2m, 10m)],
            [new Price { Date = Monday, Close = 0.125m }]);

        Assert.Equal(10.00333333m, rows[0].AverageCost);
        Assert.Equal(0.38m, rows[0].ValueBrl);
        Assert.Equal(30.01m, rows[0].CostBasisBrl);

        Assert.Equal(10.00333333m, rows[1].AverageCost);
        Assert.Equal(0.12m, rows[1].ValueBrl);
        Assert.Equal(10.00m, rows[1].CostBasisBrl);
    }

    private static IReadOnlyList<PortfolioDaily> Brl(Movement[] movements, Price[] prices) =>
        SnapshotBuilder.Build(UserId, AssetId, "BRL", movements, prices, [], Monday, LastSunday);

    private static IReadOnlyList<PortfolioDaily> Usd(Movement[] movements, Price[] prices, Benchmark[] fx) =>
        SnapshotBuilder.Build(UserId, AssetId, "USD", movements, prices, fx, Monday, Monday.AddDays(3));

    private static Price[] WeekdayCloses() =>
        Enumerable.Range(0, 14)
            .Select(Monday.AddDays)
            .Where(d => d.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
            .Select(d => new Price { Date = d, Close = Close(d) })
            .ToArray();

    private static decimal Close(DateOnly date) => 10m + date.Day / 100m;

    private static Benchmark Fx(DateOnly date, decimal value) => new() { Code = "USDBRL", Date = date, Value = value };

    private static Movement Buy(DateOnly date, decimal quantity, decimal price, decimal fees = 0m) =>
        Make(date, MovementKind.Buy, quantity, price, fees);

    private static Movement Sell(DateOnly date, decimal quantity, decimal price) =>
        Make(date, MovementKind.Sell, quantity, price, 0m);

    private static Movement Make(DateOnly date, MovementKind kind, decimal quantity, decimal price, decimal fees) =>
        new()
        {
            Id = Guid.NewGuid(),
            UserId = UserId,
            AssetId = AssetId,
            Date = date,
            Kind = kind,
            Quantity = quantity,
            UnitPrice = price,
            Fees = fees,
            CreatedAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
        };
}
