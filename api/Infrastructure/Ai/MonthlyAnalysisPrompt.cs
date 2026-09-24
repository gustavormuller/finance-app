using System.Text.RegularExpressions;

namespace Finance.Api.Infrastructure.Ai;

/// <summary>
/// The monthly analysis's system prompt: <c>Prompts/monthly-analysis.md</c>, versioned in git
/// and embedded in the assembly (009, decision 8), so what runs is what was reviewed. Line 1
/// is the version header, <c>&lt;!-- version: 1 --&gt;</c>; the rest is sent as the system
/// prompt. Changing the prompt bumps the version, which each analysis records.
/// </summary>
public sealed partial record MonthlyAnalysisPrompt(string Version, string System)
{
    /// <summary>The manifest name set in <c>Api.csproj</c>.</summary>
    public const string ResourceName = "Prompts/monthly-analysis.md";

    /// <summary><c>AiAnalysis.PromptVersion</c>'s length.</summary>
    public const int MaxVersionLength = 20;

    private static readonly Lazy<MonthlyAnalysisPrompt> Loaded = new(Load);

    /// <summary>The embedded file, read once. A missing or malformed file throws on first use.</summary>
    public static MonthlyAnalysisPrompt Current => Loaded.Value;

    /// <exception cref="InvalidOperationException">No version header on line 1, a version past the column, or no body.</exception>
    public static MonthlyAnalysisPrompt Parse(string text)
    {
        var lines = text.ReplaceLineEndings("\n").Split('\n', 2);
        var header = Header().Match(lines[0]);
        if (!header.Success || header.Groups["version"].Value.Length > MaxVersionLength)
        {
            throw new InvalidOperationException(
                $"{ResourceName} must start with '<!-- version: X -->', X at most {MaxVersionLength} letters, digits, dots or dashes.");
        }

        var body = lines.Length > 1 ? lines[1].Trim() : "";
        return body.Length == 0
            ? throw new InvalidOperationException($"{ResourceName} has a version header and nothing to send.")
            : new MonthlyAnalysisPrompt(header.Groups["version"].Value, body);
    }

    private static MonthlyAnalysisPrompt Load()
    {
        using var stream = typeof(MonthlyAnalysisPrompt).Assembly.GetManifestResourceStream(ResourceName)
            ?? throw new InvalidOperationException($"{ResourceName} is not embedded in the assembly.");
        using var reader = new StreamReader(stream);
        return Parse(reader.ReadToEnd());
    }

    [GeneratedRegex(@"^<!--\s*version:\s*(?<version>[0-9A-Za-z.\-]+)\s*-->\s*$")]
    private static partial Regex Header();
}
