using System.Text.Json;
using Finance.Api.Application.Ai;
using Finance.Api.Application.Investments;
using Finance.Api.Infrastructure.Ai;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// 009 "Prompt": <c>Infrastructure/Ai/Prompts/monthly-analysis.md</c> carries its version on
/// line 1, asks for pt-BR, the five sections, numbers from the input only, at most 400 words
/// and no preamble, and describes every field the input document has.
/// </summary>
public sealed class MonthlyAnalysisPromptTests
{
    [Fact]
    public void The_prompt_file_has_a_version_that_fits_the_column_and_it_is_not_sent()
    {
        var prompt = MonthlyAnalysisPrompt.Current;

        Assert.Matches("^[0-9A-Za-z.-]{1,20}$", prompt.Version);
        Assert.DoesNotContain("version", prompt.System.Split('\n')[0], StringComparison.OrdinalIgnoreCase);
        Assert.Equal(prompt.System.Trim(), prompt.System);
    }

    [Fact]
    public void The_prompt_asks_for_pt_br_the_five_sections_input_numbers_only_400_words_and_no_preamble()
    {
        var system = MonthlyAnalysisPrompt.Current.System;

        foreach (var phrase in new[]
        {
            "pt-BR", "## Resumo", "## Onde o dinheiro foi", "## O que mudou", "## Investimentos", "## Sugestões",
            "400 words", "preamble", "Never invent",
        })
        {
            Assert.Contains(phrase, system, StringComparison.Ordinal);
        }
    }

    [Fact]
    public void The_prompt_describes_every_field_of_the_input_document()
    {
        var input = new AnalysisAggregates(
            "2026-08",
            [new("2026-06", 0m, 0m), new("2026-07", 0m, 0m), new("2026-08", 0m, 0m)],
            [], [], [], 0m, new PortfolioSummary(0m, 0m, 0m));
        using var document = JsonDocument.Parse(AnalysisInputBuilder.Build(input));

        foreach (var field in document.RootElement.EnumerateObject())
        {
            Assert.Contains($"`{field.Name}`", MonthlyAnalysisPrompt.Current.System, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("You write an analysis.\n")]
    [InlineData("<!-- version: -->\nbody")]
    [InlineData("<!-- version: 123456789012345678901 -->\nbody")]
    [InlineData("<!-- version: 1 -->\n   \n")]
    public void A_file_without_a_usable_version_header_or_body_is_refused(string text)
    {
        Assert.Throws<InvalidOperationException>(() => MonthlyAnalysisPrompt.Parse(text));
    }

    [Fact]
    public void The_version_is_read_from_line_one_and_the_rest_is_the_prompt()
    {
        var prompt = MonthlyAnalysisPrompt.Parse("<!-- version: 2.1 -->\r\n\r\nLine one.\nLine two.\n");

        Assert.Equal(new MonthlyAnalysisPrompt("2.1", "Line one.\nLine two."), prompt);
    }
}
