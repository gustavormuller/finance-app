using Finance.Api.Domain.Transactions;

namespace Finance.Api.Domain.Import;

/// <summary>A category the suggester may pick, with the kind it must agree with.</summary>
public sealed record CategoryChoice(Guid Id, CategoryKind Kind);

/// <summary>
/// ADR-012, rung 2 and the default. Rung 1 (user rules) does not exist yet; rung 3
/// (AI) is 009.
/// </summary>
public static class CategorySuggester
{
    public static Guid? Suggest(
        decimal amount,
        CategoryChoice? history,
        CategoryChoice? defaultExpense,
        CategoryChoice? defaultIncome) =>
        throw new NotImplementedException();
}
