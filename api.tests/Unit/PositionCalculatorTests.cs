using Finance.Api.Domain.Investments;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// 007 spec unit tests 1-6 and 9: average cost, realised gain, splits and income,
/// asserted as exact <see cref="decimal"/> values with no tolerance.
/// </summary>
public sealed class PositionCalculatorTests
{
    private static readonly DateOnly Day = new(2026, 9, 1);

    /// <summary>Spec unit test 1. Fees are part of the cost basis on a buy (decision 5).</summary>
    [Fact]
    public void Buy_puts_fees_into_the_average_cost()
    {
        var position = Last(Buy(0, 100m, 10m, fees: 5m));

        Assert.Equal(100m, position.Quantity);
        Assert.Equal(10.05m, position.AverageCost);
    }

    /// <summary>Spec unit test 2.</summary>
    [Fact]
    public void Two_buys_average_by_quantity()
    {
        var position = Last(Buy(0, 100m, 10m), Buy(1, 100m, 20m));

        Assert.Equal(200m, position.Quantity);
        Assert.Equal(15m, position.AverageCost);
    }

    /// <summary>
    /// Spec unit test 3. Realised gain is <c>(price x qty - fees) - avg x qty</c>:
    /// (12 x 50 - 3) - 10.05 x 50 = 597 - 502.5 = 94.5.
    /// </summary>
    [Fact]
    public void Sell_keeps_the_average_and_realises_the_gain_net_of_fees()
    {
        var position = Last(Buy(0, 100m, 10m, fees: 5m), Sell(1, 50m, 12m, fees: 3m));

        Assert.Equal(50m, position.Quantity);
        Assert.Equal(10.05m, position.AverageCost);
        Assert.Equal(94.5m, position.RealisedGain);
    }

    /// <summary>Spec unit test 4. A closed position leaves no average behind.</summary>
    [Fact]
    public void Selling_everything_then_buying_starts_a_fresh_average()
    {
        var steps = PositionCalculator.Calculate(
            [Buy(0, 100m, 10m), Sell(1, 100m, 12m), Buy(2, 10m, 30m)]);

        Assert.Equal(0m, steps[1].Position.Quantity);
        Assert.Equal(0m, steps[1].Position.AverageCost);
        Assert.Equal(0m, steps[1].Position.CostBasisBrl);

        Assert.Equal(10m, steps[2].Position.Quantity);
        Assert.Equal(30m, steps[2].Position.AverageCost);
        Assert.Equal(200m, steps[2].Position.RealisedGain);
    }

    /// <summary>
    /// Spec unit test 5. A 1:2 split on 100 shares is <c>Quantity = 100, UnitPrice = 0</c>
    /// (decision 3): the quantity doubles and the cost does not move.
    /// </summary>
    [Fact]
    public void Split_doubles_the_quantity_and_halves_the_average()
    {
        var before = Last(Buy(0, 100m, 10m));
        var after = Last(Buy(0, 100m, 10m), Make(1, MovementKind.Split, 100m, 0m));

        Assert.Equal(200m, after.Quantity);
        Assert.Equal(5m, after.AverageCost);
        Assert.Equal(before.Quantity * before.AverageCost, after.Quantity * after.AverageCost);
        Assert.Equal(before.CostBasisBrl, after.CostBasisBrl);
    }

    /// <summary>Spec unit test 6, for both income kinds.</summary>
    [Theory]
    [InlineData(MovementKind.Dividend)]
    [InlineData(MovementKind.Jcp)]
    public void Income_leaves_quantity_and_average_alone(MovementKind kind)
    {
        var income = Make(1, kind, 0m, 0m);
        income.Amount = 120m;

        var position = Last(Buy(0, 100m, 10m), income);

        Assert.Equal(100m, position.Quantity);
        Assert.Equal(10m, position.AverageCost);
        Assert.Equal(120m, position.Income);
    }

    /// <summary>
    /// Spec unit test 9. Three-decimal quantities and eight-decimal prices, against
    /// values worked out by hand at full precision:
    /// avg = (1.125 x 12.34567891 + 1.375 x 23.45678912 + 0.75) / 2.5 = 18.7567895255,
    /// realised = (20.12345678 x 0.875 - 0.40) - 18.7567895255 x 0.875 = 0.7958338476875.
    /// </summary>
    [Fact]
    public void Fractional_quantities_and_eight_place_prices_are_exact()
    {
        var position = Last(
            Buy(0, 1.125m, 12.34567891m),
            Buy(1, 1.375m, 23.45678912m, fees: 0.75m),
            Sell(2, 0.875m, 20.12345678m, fees: 0.40m));

        Assert.Equal(1.625m, position.Quantity);
        Assert.Equal(18.7567895255m, position.AverageCost);
        Assert.Equal(0.7958338476875m, position.RealisedGain);
    }

    /// <summary>Same-day movements apply in <c>CreatedAt</c> order, not list order.</summary>
    [Fact]
    public void Movements_are_applied_by_date_then_creation()
    {
        var buy = Buy(0, 100m, 10m);
        var sell = Sell(0, 40m, 12m);
        sell.CreatedAt = buy.CreatedAt.AddMinutes(1);

        var steps = PositionCalculator.Calculate([sell, Buy(-1, 10m, 10m), buy]);

        Assert.Equal([MovementKind.Buy, MovementKind.Buy, MovementKind.Sell], steps.Select(s => s.Movement.Kind));
        Assert.Equal(70m, steps[^1].Position.Quantity);
    }

    private static Position Last(params Movement[] movements) =>
        PositionCalculator.Calculate(movements)[^1].Position;

    private static Movement Buy(int day, decimal quantity, decimal price, decimal fees = 0m) =>
        Make(day, MovementKind.Buy, quantity, price, fees);

    private static Movement Sell(int day, decimal quantity, decimal price, decimal fees = 0m) =>
        Make(day, MovementKind.Sell, quantity, price, fees);

    private static Movement Make(int day, MovementKind kind, decimal quantity, decimal price, decimal fees = 0m) =>
        new()
        {
            Id = Guid.NewGuid(),
            Date = Day.AddDays(day),
            Kind = kind,
            Quantity = quantity,
            UnitPrice = price,
            Fees = fees,
            Currency = "BRL",
            CreatedAt = new DateTimeOffset(2026, 9, 1, 12, 0, 0, TimeSpan.Zero),
        };
}
