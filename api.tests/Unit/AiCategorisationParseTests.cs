using Finance.Api.Domain.Import;
using Finance.Api.Domain.Transactions;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// 009 spec unit tests 5-10: the model's answer is untrusted text. Only a sent row, an owned
/// category and a kind that agrees with the row's sign survive; nothing throws.
/// </summary>
public sealed class AiCategorisationParseTests
{
    private static readonly Guid Groceries = Guid.Parse("11111111-1111-1111-1111-111111111111");
    private static readonly Guid Salary = Guid.Parse("22222222-2222-2222-2222-222222222222");
    private static readonly Guid OwnTransfer = Guid.Parse("33333333-3333-3333-3333-333333333333");
    private static readonly Guid Debit = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Credit = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");

    private static readonly Dictionary<Guid, decimal> Rows = new() { [Debit] = -42.90m, [Credit] = 3000m };

    private static readonly CategoryChoice[] Categories =
    [
        new(Groceries, CategoryKind.Expense),
        new(Salary, CategoryKind.Income),
        new(OwnTransfer, CategoryKind.Transfer),
    ];

    private static string Answer(params (Guid Row, Guid Category)[] pairs) =>
        "{" + string.Join(",", pairs.Select(pair => $"\"{pair.Row}\":\"{pair.Category}\"")) + "}";

    private static IReadOnlyDictionary<Guid, Guid> Parse(string? text) => AiCategorisation.Parse(text, Rows, Categories);

    /// <summary>Spec test 5.</summary>
    [Fact]
    public void Clean_json_maps_every_row()
    {
        var parsed = Parse(Answer((Debit, Groceries), (Credit, Salary)));

        Assert.Equal(new Dictionary<Guid, Guid> { [Debit] = Groceries, [Credit] = Salary }, parsed);
    }

    /// <summary>Spec test 6.</summary>
    [Theory]
    [InlineData("```json\n{0}\n```")]
    [InlineData("```\n{0}\n```")]
    [InlineData("Aqui está o resultado:\n\n```json\n{0}\n```\nEspero ter ajudado.")]
    [InlineData("Resultado: {0} (fim)")]
    [InlineData("  \n{0}\n  ")]
    public void Fenced_or_surrounded_json_is_still_parsed(string template)
    {
        var parsed = Parse(template.Replace("{0}", Answer((Debit, Groceries), (Credit, Salary))));

        Assert.Equal(new Dictionary<Guid, Guid> { [Debit] = Groceries, [Credit] = Salary }, parsed);
    }

    /// <summary>Spec test 7.</summary>
    [Fact]
    public void An_unknown_row_id_is_ignored()
    {
        var parsed = Parse(Answer((Guid.NewGuid(), Groceries), (Debit, Groceries)));

        Assert.Equal(new Dictionary<Guid, Guid> { [Debit] = Groceries }, parsed);
    }

    /// <summary>Spec test 8: somebody else's category, or one the model made up.</summary>
    [Fact]
    public void A_category_id_the_user_does_not_own_is_ignored()
    {
        var parsed = Parse(Answer((Debit, Guid.NewGuid()), (Credit, Salary)));

        Assert.Equal(new Dictionary<Guid, Guid> { [Credit] = Salary }, parsed);
    }

    /// <summary>Spec test 9: rule 3 still applies to what the AI suggests.</summary>
    [Fact]
    public void A_category_kind_that_disagrees_with_the_sign_is_ignored()
    {
        var parsed = Parse(Answer((Debit, Salary), (Credit, Groceries)));

        Assert.Empty(parsed);
    }

    /// <summary>Rule 3 as amended in 005: a transfer takes either sign.</summary>
    [Fact]
    public void A_transfer_is_kept_on_either_sign()
    {
        var parsed = Parse(Answer((Debit, OwnTransfer), (Credit, OwnTransfer)));

        Assert.Equal(new Dictionary<Guid, Guid> { [Debit] = OwnTransfer, [Credit] = OwnTransfer }, parsed);
    }

    /// <summary>Spec test 10.</summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("Não consegui categorizar estas transações.")]
    [InlineData("## Resumo\n\nResposta de teste do provedor simulado.")]
    [InlineData("{ \"aaaaaaaa-0000-0000-0000-000000000001\": ")]
    [InlineData("[\"11111111-1111-1111-1111-111111111111\"]")]
    [InlineData("\"11111111-1111-1111-1111-111111111111\"")]
    [InlineData("{}")]
    [InlineData("}{")]
    [InlineData("{\"rows\": {\"aaaaaaaa-0000-0000-0000-000000000001\": \"11111111-1111-1111-1111-111111111111\"}}")]
    public void Garbage_changes_no_row_and_does_not_throw(string? text)
    {
        Assert.Empty(Parse(text));
    }

    [Fact]
    public void A_value_that_is_not_a_category_id_string_drops_that_row_only()
    {
        var text = $"{{\"{Debit}\": 42, \"{Credit}\": \"{Salary}\", \"{Guid.NewGuid()}\": null}}";

        Assert.Equal(new Dictionary<Guid, Guid> { [Credit] = Salary }, Parse(text));
    }

    [Fact]
    public void A_row_answered_twice_keeps_its_first_answer()
    {
        var text = $"{{\"{Debit}\": \"{Groceries}\", \"{Debit}\": \"{OwnTransfer}\"}}";

        Assert.Equal(new Dictionary<Guid, Guid> { [Debit] = Groceries }, Parse(text));
    }

    [Fact]
    public void Ids_are_read_in_any_case_and_a_trailing_comma_is_tolerated()
    {
        var text = $"{{\"{Debit.ToString().ToUpperInvariant()}\": \"{Groceries.ToString().ToUpperInvariant()}\",}}";

        Assert.Equal(new Dictionary<Guid, Guid> { [Debit] = Groceries }, Parse(text));
    }
}
