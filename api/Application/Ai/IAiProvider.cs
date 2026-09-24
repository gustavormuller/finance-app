namespace Finance.Api.Application.Ai;

/// <summary>
/// An AI provider's completion call (ADR-015: Anthropic and OpenAI, config-selected; a fake
/// for tests and E2E). Nothing but <see cref="AiGateway"/> calls it, so no call escapes the
/// budget or the usage record.
/// </summary>
public interface IAiProvider
{
    /// <exception cref="AiProviderException">The provider failed, with what it reported spending.</exception>
    Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct);
}

public sealed record AiRequest(string Model, string System, string User, int MaxTokens);

public sealed record AiCompletion(string Text, int InputTokens, int OutputTokens);

/// <summary>
/// A provider call that failed. <see cref="InputTokens"/> is what the provider reported
/// spending, <c>0</c> when it says nothing was billed (a refused key, say), and <c>null</c>
/// when it is unknown (a timeout): <see cref="AiGateway"/> then records an estimate.
/// </summary>
public class AiProviderException(string message, int? inputTokens = null, int outputTokens = 0, Exception? innerException = null)
    : Exception(message, innerException)
{
    public int? InputTokens { get; } = inputTokens;

    public int OutputTokens { get; } = outputTokens;
}

/// <summary>
/// The call ran past its purpose's <c>TimeoutSeconds</c> (<see cref="AiGateway"/>). Its tokens
/// are unknown, so the gateway records an estimate. The suggest endpoint answers <c>504</c> (CP4).
/// </summary>
public sealed class AiProviderTimeoutException(string message, Exception? innerException = null)
    : AiProviderException(message, inputTokens: null, outputTokens: 0, innerException);

/// <summary>The user has spent their month's budget (ADR-008). The endpoints answer <c>402</c>.</summary>
public sealed class AiBudgetExceededException(decimal spentBrl, decimal budgetBrl)
    : Exception($"AI budget exceeded: spent {spentBrl} BRL of {budgetBrl} BRL this month.")
{
    public decimal SpentBrl { get; } = spentBrl;

    public decimal BudgetBrl { get; } = budgetBrl;
}

/// <summary>The user has not turned on <c>ai_enabled</c> (ADR-010). The endpoints answer <c>403</c>.</summary>
public sealed class AiDisabledException() : Exception("AI is not enabled for this user.");
