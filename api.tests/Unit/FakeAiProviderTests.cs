using System.Text.Json;
using Finance.Api.Application.Ai;
using Finance.Api.Domain.Import;
using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure.Ai;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// 009 checkpoint 2: the E2E run's provider. Fixed, network-free and deterministic; its
/// token counts follow the request, so the budget and usage paths see real numbers.
/// </summary>
public sealed class FakeAiProviderTests
{
    [Fact]
    public async Task It_answers_the_same_request_the_same_way_in_pt_br_markdown()
    {
        var request = new AiRequest("any-model", "sistema", "dados", 1000);

        var first = await new FakeAiProvider().CompleteAsync(request, TestContext.Current.CancellationToken);
        var second = await new FakeAiProvider().CompleteAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(first, second);
        Assert.StartsWith("## Resumo", first.Text);
    }

    /// <summary>For tests 21-23 and E2E 29: the prompt's five sections, and the month and its expense from the input.</summary>
    [Fact]
    public async Task An_analysis_request_is_answered_with_the_five_sections_and_numbers_from_the_input()
    {
        var input = AnalysisInputBuilder.Build(new AnalysisAggregates(
            "2026-08",
            [new("2026-06", 0m, 0m), new("2026-07", 10m, -20m), new("2026-08", 5900m, -2800.5m)],
            [], [], [], 0m, new Application.Investments.PortfolioSummary(0m, 0m, 0m)));
        var request = new AiRequest("any-model", MonthlyAnalysisPrompt.Current.System, input, 8000);

        var first = await new FakeAiProvider().CompleteAsync(request, TestContext.Current.CancellationToken);
        var second = await new FakeAiProvider().CompleteAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(first, second);
        var headings = first.Text.Split('\n').Where(line => line.StartsWith("## ", StringComparison.Ordinal));
        Assert.Equal(["## Resumo", "## Onde o dinheiro foi", "## O que mudou", "## Investimentos", "## Sugestões"], headings);
        Assert.Contains("2026-08", first.Text, StringComparison.Ordinal);
        Assert.Contains("2800.50", first.Text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Its_tokens_are_four_characters_each_rounded_up()
    {
        var request = new AiRequest("any-model", new string('s', 10), new string('u', 7), 1000);

        var completion = await new FakeAiProvider().CompleteAsync(request, TestContext.Current.CancellationToken);

        Assert.Equal(5, completion.InputTokens); // 17 characters
        Assert.Equal((completion.Text.Length + 3) / 4, completion.OutputTokens);
    }

    [Fact]
    public async Task A_cancelled_call_is_cancelled()
    {
        var request = new AiRequest("any-model", "sistema", "dados", 1000);

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            new FakeAiProvider().CompleteAsync(request, new CancellationToken(canceled: true)));
    }

    private static readonly Guid Food = Guid.NewGuid();
    private static readonly Guid Other = Guid.NewGuid();
    private static readonly Guid Salary = Guid.NewGuid();
    private static readonly Guid Leisure = Guid.NewGuid();

    private static AiRequest Categorisation(params AiCategorisationRow[] rows) => new(
        "any-model",
        AiCategorisation.System,
        AiCategorisation.BuildRequest(
            rows,
            [
                new(Food, "Alimentação", CategoryKind.Expense),
                new(Leisure, "Lazer", CategoryKind.Expense),
                new(Salary, "Salário", CategoryKind.Income),
                new(Other, DefaultCategories.OtherExpenseName, CategoryKind.Expense),
            ]),
        AiCategorisation.MaxTokensFor(rows.Length));

    /// <summary>
    /// 009 CP4b, for spec tests 18-19 and E2E 28: each row gets the first category of its kind
    /// that is not a sign default, as a model's <c>{ rowId: categoryId }</c> would.
    /// </summary>
    [Fact]
    public async Task A_categorisation_request_is_answered_with_a_category_of_each_rows_kind()
    {
        var debit = Guid.NewGuid();
        var credit = Guid.NewGuid();

        var completion = await new FakeAiProvider().CompleteAsync(
            Categorisation(new(debit, "PADARIA REAL", -19.90m), new(credit, "TED RECEBIDA", 3000m)),
            TestContext.Current.CancellationToken);

        Assert.Equal(
            new Dictionary<string, string> { [debit.ToString()] = Food.ToString(), [credit.ToString()] = Salary.ToString() },
            JsonSerializer.Deserialize<Dictionary<string, string>>(completion.Text));
    }

    /// <summary>Spec test 20: a row whose description holds the marker gets the markdown, not JSON.</summary>
    [Fact]
    public async Task A_categorisation_request_with_the_garbage_marker_is_answered_with_garbage()
    {
        var completion = await new FakeAiProvider().CompleteAsync(
            Categorisation(new AiCategorisationRow(Guid.NewGuid(), $"LOJA {FakeAiProvider.GarbageMarker}", -10m)),
            TestContext.Current.CancellationToken);

        Assert.StartsWith("## Resumo", completion.Text);
    }
}
