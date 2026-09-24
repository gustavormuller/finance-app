using Finance.Api.Domain.Import;
using Finance.Api.Domain.Transactions;

namespace Finance.Api.Tests.Unit;

/// <summary>Spec unit tests 33-35.</summary>
public sealed class CategorySuggesterTests
{
    private static readonly CategoryChoice Food = new(Guid.NewGuid(), CategoryKind.Expense);
    private static readonly CategoryChoice Salary = new(Guid.NewGuid(), CategoryKind.Income);
    private static readonly CategoryChoice Outros = new(Guid.NewGuid(), CategoryKind.Expense);
    private static readonly CategoryChoice OutrasReceitas = new(Guid.NewGuid(), CategoryKind.Income);
    private static readonly CategoryChoice Transferencia = new(Guid.NewGuid(), CategoryKind.Transfer);

    /// <summary>Spec unit test 33.</summary>
    [Fact]
    public void History_match_is_applied() =>
        Assert.Equal(Food.Id, CategorySuggester.Suggest(-42.90m, Food, Outros, OutrasReceitas));

    /// <summary>
    /// Spec unit test 34. A refund from a shop last filed under Food is positive; 003's
    /// rule 3 would refuse Food for it, so the suggester must not offer it.
    /// </summary>
    [Fact]
    public void History_match_of_the_wrong_kind_is_rejected_and_the_default_used()
    {
        Assert.Equal(OutrasReceitas.Id, CategorySuggester.Suggest(42.90m, Food, Outros, OutrasReceitas));
        Assert.Equal(Outros.Id, CategorySuggester.Suggest(-3000m, Salary, Outros, OutrasReceitas));
    }

    /// <summary>Spec unit test 35.</summary>
    [Fact]
    public void No_history_falls_back_to_the_sign_default()
    {
        Assert.Equal(Outros.Id, CategorySuggester.Suggest(-1m, null, Outros, OutrasReceitas));
        Assert.Equal(OutrasReceitas.Id, CategorySuggester.Suggest(1m, null, Outros, OutrasReceitas));
    }

    /// <summary>
    /// Only possible if the user deleted the seeded defaults. Null, so the row becomes
    /// Invalid with "Categoria não encontrada" rather than filed anywhere at random.
    /// </summary>
    [Fact]
    public void Nothing_resolves_when_the_defaults_are_gone()
    {
        Assert.Null(CategorySuggester.Suggest(-1m, null, null, OutrasReceitas));
        Assert.Null(CategorySuggester.Suggest(1m, null, Outros, null));
    }

    /// <summary>A renamed default of the wrong kind is no default at all.</summary>
    [Fact]
    public void A_default_of_the_wrong_kind_is_not_used()
    {
        Assert.Null(CategorySuggester.Suggest(-1m, null, defaultExpense: OutrasReceitas, defaultIncome: null));
        Assert.Null(CategorySuggester.Suggest(1m, null, defaultExpense: null, defaultIncome: Outros));
    }

    /// <summary>Zero is not a movement; the row is Invalid before suggestion matters.</summary>
    [Fact]
    public void Zero_gets_no_suggestion() =>
        Assert.Null(CategorySuggester.Suggest(0m, Food, Outros, OutrasReceitas));

    /// <summary>
    /// 005 amendment 2. "PAGAMENTO FATURA" filed once as a transfer is a transfer next
    /// time, whichever side of it the statement shows.
    /// </summary>
    [Fact]
    public void A_transfer_history_match_is_kept_for_either_sign()
    {
        Assert.Equal(Transferencia.Id, CategorySuggester.Suggest(-1500m, Transferencia, Outros, OutrasReceitas));
        Assert.Equal(Transferencia.Id, CategorySuggester.Suggest(1500m, Transferencia, Outros, OutrasReceitas));
    }

    /// <summary>005 amendment 2. The sign default never lands on a transfer category.</summary>
    [Fact]
    public void The_sign_default_never_picks_a_transfer()
    {
        Assert.Null(CategorySuggester.Suggest(-1m, null, defaultExpense: Transferencia, defaultIncome: OutrasReceitas));
        Assert.Null(CategorySuggester.Suggest(1m, null, defaultExpense: Outros, defaultIncome: Transferencia));
    }
}
