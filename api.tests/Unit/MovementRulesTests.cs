using Finance.Api.Domain.Investments;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// 007 spec unit tests 7 and 8, and the rest of the spec's validation table. Messages
/// are asserted verbatim: the API renders them on screen as they are.
/// </summary>
public sealed class MovementRulesTests
{
    private static readonly DateOnly Today = new(2026, 9, 24);

    private static readonly DateTimeOffset Noon = new(2026, 9, 1, 12, 0, 0, TimeSpan.Zero);

    /// <summary>Spec unit test 7.</summary>
    [Fact]
    public void Selling_more_than_is_held_is_refused()
    {
        var violation = MovementRules.ValidatePositions(
            [Make(Today.AddDays(-10), MovementKind.Buy, 100m), Make(Today.AddDays(-5), MovementKind.Sell, 150m)]);

        Assert.Equal(MovementRules.QuantityField, violation?.Field);
        Assert.Equal("Quantidade vendida maior que a posição", violation?.Message);
    }

    [Fact]
    public void Selling_everything_held_is_allowed() =>
        Assert.Null(MovementRules.ValidatePositions(
            [Make(Today.AddDays(-10), MovementKind.Buy, 100m), Make(Today.AddDays(-5), MovementKind.Sell, 100m)]));

    /// <summary>
    /// Spec unit test 8. The inserted sell fits the position on its own date, but the
    /// later sell no longer does.
    /// </summary>
    [Fact]
    public void An_old_sell_that_makes_a_later_position_negative_is_refused()
    {
        var history = new List<Movement>
        {
            Make(Today.AddDays(-30), MovementKind.Buy, 100m),
            Make(Today.AddDays(-10), MovementKind.Sell, 80m),
        };
        Assert.Null(MovementRules.ValidatePositions(history));

        history.Add(Make(Today.AddDays(-20), MovementKind.Sell, 50m));

        Assert.Equal(
            "Quantidade vendida maior que a posição",
            MovementRules.ValidatePositions(history)?.Message);
    }

    /// <summary>Same date: the order is <c>CreatedAt</c>, so a sell entered before its buy is refused.</summary>
    [Fact]
    public void Same_day_order_is_creation_order()
    {
        var sell = Make(Today, MovementKind.Sell, 10m);
        var buy = Make(Today, MovementKind.Buy, 10m);
        buy.CreatedAt = sell.CreatedAt.AddSeconds(1);

        Assert.NotNull(MovementRules.ValidatePositions([buy, sell]));

        buy.CreatedAt = sell.CreatedAt.AddSeconds(-1);
        Assert.Null(MovementRules.ValidatePositions([buy, sell]));
    }

    [Theory]
    [InlineData(MovementKind.Buy, "0")]
    [InlineData(MovementKind.Sell, "-1")]
    [InlineData(MovementKind.Split, "0")]
    public void Quantity_must_be_positive(MovementKind kind, string quantity)
    {
        var movement = Make(Today, kind, decimal.Parse(quantity, System.Globalization.CultureInfo.InvariantCulture));

        Assert.Contains(
            MovementRules.Validate(movement, "BRL", Today),
            v => v is { Field: MovementRules.QuantityField, Message: "Quantidade deve ser positiva" });
    }

    [Theory]
    [InlineData(MovementKind.Dividend, "0")]
    [InlineData(MovementKind.Jcp, "-0.01")]
    public void Income_amount_must_be_positive(MovementKind kind, string amount)
    {
        var movement = Make(Today, kind, 0m);
        movement.Amount = decimal.Parse(amount, System.Globalization.CultureInfo.InvariantCulture);

        Assert.Equal(
            [new(MovementRules.AmountField, "Valor deve ser positivo")],
            MovementRules.Validate(movement, "BRL", Today));
    }

    [Fact]
    public void Currency_must_equal_the_market_asset()
    {
        var movement = Make(Today, MovementKind.Buy, 1m);

        Assert.Empty(MovementRules.Validate(movement, "BRL", Today));
        Assert.Equal(
            [new(MovementRules.CurrencyField, "Moeda diferente do ativo")],
            MovementRules.Validate(movement, "USD", Today));
    }

    /// <summary>Both bounds inclusive: 1990-01-01 and today pass, the day outside each does not.</summary>
    [Fact]
    public void Date_is_between_1990_and_today()
    {
        Assert.Empty(MovementRules.Validate(Make(new DateOnly(1990, 1, 1), MovementKind.Buy, 1m), "BRL", Today));
        Assert.Empty(MovementRules.Validate(Make(Today, MovementKind.Buy, 1m), "BRL", Today));

        foreach (var date in new[] { new DateOnly(1989, 12, 31), Today.AddDays(1) })
        {
            Assert.Equal(
                [new(MovementRules.DateField, "Data fora do intervalo")],
                MovementRules.Validate(Make(date, MovementKind.Buy, 1m), "BRL", Today));
        }
    }

    /// <summary>Data model: <c>Fees &gt;= 0</c>. Zero is allowed.</summary>
    [Fact]
    public void Fees_cannot_be_negative()
    {
        var movement = Make(Today, MovementKind.Buy, 1m);
        Assert.Empty(MovementRules.Validate(movement, "BRL", Today));

        movement.Fees = -0.01m;
        Assert.Equal(
            [new(MovementRules.FeesField, "Taxas não podem ser negativas")],
            MovementRules.Validate(movement, "BRL", Today));
    }

    private static Movement Make(DateOnly date, MovementKind kind, decimal quantity) =>
        new()
        {
            Id = Guid.NewGuid(),
            Date = date,
            Kind = kind,
            Quantity = quantity,
            UnitPrice = kind == MovementKind.Split ? 0m : 10m,
            Currency = "BRL",
            CreatedAt = Noon,
        };
}
