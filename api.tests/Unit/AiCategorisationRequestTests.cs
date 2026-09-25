using System.Text.Json;
using Finance.Api.Domain.Import;
using Finance.Api.Domain.Transactions;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// 009 rung 3, what is sent: one JSON document of <c>{ rowId, description }</c> rows and the
/// category list (spec), each row with the kind its sign allows, and no amounts. The answer's
/// ceiling follows the row count, since a truncated answer fails.
/// </summary>
public sealed class AiCategorisationRequestTests
{
    private static readonly Guid Debit = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000001");
    private static readonly Guid Credit = Guid.Parse("aaaaaaaa-0000-0000-0000-000000000002");
    private static readonly Guid Food = Guid.Parse("11111111-1111-1111-1111-111111111111");

    [Fact]
    public void The_request_lists_the_rows_with_their_kind_and_the_categories_and_no_amount()
    {
        var text = AiCategorisation.BuildRequest(
            [new(Debit, "PAG IFOOD", -42.90m), new(Credit, "TED RECEBIDA", 3000m)],
            [new(Food, "Alimentação > Restaurantes", CategoryKind.Expense)]);

        using var request = JsonDocument.Parse(text);
        var rows = request.RootElement.GetProperty("rows").EnumerateArray().ToList();
        Assert.Equal(
            [(Debit.ToString(), "PAG IFOOD", "Expense"), (Credit.ToString(), "TED RECEBIDA", "Income")],
            rows.Select(row => (row.GetProperty("rowId").GetString(), row.GetProperty("description").GetString(), row.GetProperty("kind").GetString())));
        Assert.All(rows, row => Assert.Equal(3, row.EnumerateObject().Count()));
        var category = Assert.Single(request.RootElement.GetProperty("categories").EnumerateArray().ToList());
        Assert.Equal(
            (Food.ToString(), "Alimentação > Restaurantes", "Expense"),
            (category.GetProperty("id").GetString(), category.GetProperty("name").GetString(), category.GetProperty("kind").GetString()));
        Assert.DoesNotContain("42", text);
        Assert.DoesNotContain("3000", text);
        Assert.Contains("Alimentação", text); // unescaped: fewer tokens, and readable in a log
    }

    [Fact]
    public void The_system_prompt_asks_for_a_bare_object_of_row_ids_to_category_ids()
    {
        Assert.Contains("rowId", AiCategorisation.System);
        Assert.Contains("JSON object", AiCategorisation.System);
        Assert.Contains("kind", AiCategorisation.System);
    }

    /// <summary>
    /// About 64 tokens a pair (two GUIDs and punctuation) and 256 for the frame, so a full
    /// batch fits well inside what the categorisation model answers in 30 s.
    /// </summary>
    [Theory]
    [InlineData(1, 320)]
    [InlineData(10, 896)]
    [InlineData(AiCategorisation.BatchSize, 2816)]
    public void The_answer_ceiling_grows_with_the_rows_sent(int rows, int maxTokens)
    {
        Assert.Equal(maxTokens, AiCategorisation.MaxTokensFor(rows));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(AiCategorisation.BatchSize + 1)]
    public void A_count_outside_one_batch_is_refused(int rows)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => AiCategorisation.MaxTokensFor(rows));
    }
}
