namespace Finance.Api.Domain.Ai;

/// <summary>
/// A user's AI analysis of one month, and the state of the job that writes it. One per
/// user per month: regenerating replaces the row (unique <c>(UserId, Month)</c>).
/// </summary>
public sealed class AiAnalysis : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    /// <summary>The month analysed, <c>YYYY-MM</c>.</summary>
    public string Month { get; set; } = "";

    public AiAnalysisStatus Status { get; set; }

    /// <summary>Markdown in pt-BR, as the provider wrote it. Null until <see cref="AiAnalysisStatus.Completed"/>.</summary>
    public string? Content { get; set; }

    /// <summary>Why the job failed, at most 500 characters. Null unless <see cref="AiAnalysisStatus.Failed"/>.</summary>
    public string? Error { get; set; }

    /// <summary>The version header on line 1 of the prompt file that produced the content.</summary>
    public string PromptVersion { get; set; } = "";

    public DateTimeOffset CreatedAt { get; set; }

    public DateTimeOffset? StartedAt { get; set; }

    public DateTimeOffset? CompletedAt { get; set; }
}
