using Finance.Api.Domain.Transactions;

namespace Finance.Api.Domain.Investments;

/// <summary>007's movement validation rules. Pure: <c>today</c> is an argument.</summary>
public static class MovementRules
{
    public const string QuantityField = "quantity";

    public const string AmountField = "amount";

    public const string FeesField = "fees";

    public const string CurrencyField = "currency";

    public const string DateField = "date";

    public static readonly DateOnly MinimumDate = new(1990, 1, 1);

    public static IReadOnlyList<RuleViolation> Validate(Movement movement, string assetCurrency, DateOnly today) =>
        throw new NotImplementedException();

    public static RuleViolation? ValidatePositions(IEnumerable<Movement> movements) =>
        throw new NotImplementedException();
}
