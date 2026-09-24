using Finance.Api.Domain.Ai;

namespace Finance.Api.Application.Ai;

/// <summary>
/// Every AI call goes through here: the <c>ai_enabled</c> gate, the budget, the call, and
/// the usage row, written whether the call succeeded or not (009, decision 3).
/// </summary>
public sealed class AiGateway
{
    public Task<AiCompletion> CompleteAsync(AiPurpose purpose, string system, string user, int maxTokens, CancellationToken ct) =>
        throw new NotImplementedException();
}
