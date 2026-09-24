using Finance.Api.Domain.Transactions;

namespace Finance.Api.Domain.Investments;

/// <summary>
/// 007's movement validation rules. Pure functions of their arguments, so they are
/// unit-testable without a database (ADR-014); <c>today</c> is an argument, never the
/// clock.
/// </summary>
/// <remarks>
/// Field names are wire identifiers and stay English. Messages are pt-BR and are
/// rendered verbatim, so they are the spec's table word for word. The caller loads the
/// asset's currency and its movement history; deciding is all that happens here.
/// </remarks>
public static class MovementRules
{
    public const string QuantityField = "quantity";

    public const string AmountField = "amount";

    public const string FeesField = "fees";

    public const string CurrencyField = "currency";

    public const string DateField = "date";

    /// <summary>The lower bound of the date rule, inclusive.</summary>
    public static readonly DateOnly MinimumDate = new(1990, 1, 1);

    private const string OversoldMessage = "Quantidade vendida maior que a posição";

    /// <summary>
    /// Every rule that needs only the movement, its asset's currency and today. Empty
    /// when the movement is acceptable on its own. Whether it fits the history is
    /// <see cref="ValidatePositions"/>.
    /// </summary>
    public static IReadOnlyList<RuleViolation> Validate(Movement movement, string assetCurrency, DateOnly today)
    {
        var violations = new List<RuleViolation>();

        var needsQuantity = movement.Kind is MovementKind.Buy or MovementKind.Sell or MovementKind.Split;
        if (needsQuantity && movement.Quantity <= 0m)
        {
            violations.Add(new RuleViolation(QuantityField, "Quantidade deve ser positiva"));
        }

        var isIncome = movement.Kind is MovementKind.Dividend or MovementKind.Jcp;
        if (isIncome && movement.Amount <= 0m)
        {
            violations.Add(new RuleViolation(AmountField, "Valor deve ser positivo"));
        }

        // The data model's "Fees >= 0". The spec's table has no message for it.
        if (movement.Fees < 0m)
        {
            violations.Add(new RuleViolation(FeesField, "Taxas não podem ser negativas"));
        }

        // Ordinal: "brl" is not "BRL". The API normalises the code before it gets here.
        if (!string.Equals(movement.Currency, assetCurrency, StringComparison.Ordinal))
        {
            violations.Add(new RuleViolation(CurrencyField, "Moeda diferente do ativo"));
        }

        if (movement.Date < MinimumDate || movement.Date > today)
        {
            violations.Add(new RuleViolation(DateField, "Data fora do intervalo"));
        }

        return violations;
    }

    /// <summary>
    /// Whether the whole history, with the movement being written already in it, keeps
    /// every position non-negative. Replaying the whole history is what refuses an old
    /// sell that leaves a <em>later</em> sell uncovered (spec test 8).
    /// </summary>
    /// <param name="movements">
    /// Every movement of one asset, after the insert, update or delete. Replayed by
    /// date, then <c>CreatedAt</c>.
    /// </param>
    public static RuleViolation? ValidatePositions(IEnumerable<Movement> movements)
    {
        var position = default(Position);

        foreach (var movement in PositionCalculator.InOrder(movements))
        {
            if (movement.Kind == MovementKind.Sell && movement.Quantity > position.Quantity)
            {
                return new RuleViolation(QuantityField, OversoldMessage);
            }

            // The rate is irrelevant to quantity, so 1 will do.
            position = PositionCalculator.Apply(position, movement, 1m);
        }

        return null;
    }
}
