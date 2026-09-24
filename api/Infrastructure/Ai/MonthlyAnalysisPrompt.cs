namespace Finance.Api.Infrastructure.Ai;

/// <summary>The monthly analysis's system prompt, <c>Prompts/monthly-analysis.md</c>, and its version header.</summary>
public sealed record MonthlyAnalysisPrompt(string Version, string System)
{
    public static MonthlyAnalysisPrompt Current => throw new NotImplementedException();

    public static MonthlyAnalysisPrompt Parse(string text) => throw new NotImplementedException();
}
