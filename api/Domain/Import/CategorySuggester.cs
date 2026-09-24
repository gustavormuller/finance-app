using Finance.Api.Domain.Transactions;

namespace Finance.Api.Domain.Import;

/// <summary>A category the suggester may pick, with the kind it must agree with.</summary>
public sealed record CategoryChoice(Guid Id, CategoryKind Kind);

/// <summary>
/// ADR-012, rung 2 and the default. Rung 1 (user rules) does not exist yet; rung 3
/// (AI) is 009.
/// </summary>
/// <remarks>
/// The caller resolves the inputs — the most recent committed transaction with the
/// same normalized description, and the two defaults by name among the user's
/// top-level categories — so this stays a pure function of a sign and three
/// candidates.
/// </remarks>
public static class CategorySuggester
{
    /// <summary>
    /// Null when nothing acceptable exists, which makes the row Invalid with
    /// "Categoria não encontrada" rather than filed somewhere at random. Every
    /// candidate is held to 003's rule 3: its kind has to agree with the sign, or the
    /// commit would be refused anyway.
    /// </summary>
    public static Guid? Suggest(
        decimal amount,
        CategoryChoice? history,
        CategoryChoice? defaultExpense,
        CategoryChoice? defaultIncome)
    {
        if (amount == 0m)
        {
            return null;
        }

        if (history is { Kind: CategoryKind.Transfer })
        {
            throw new NotImplementedException();
        }

        var kind = amount < 0m ? CategoryKind.Expense : CategoryKind.Income;

        if (history is { } remembered && remembered.Kind == kind)
        {
            return remembered.Id;
        }

        var fallback = kind == CategoryKind.Expense ? defaultExpense : defaultIncome;

        return fallback is { } chosen && chosen.Kind == kind ? chosen.Id : null;
    }
}
