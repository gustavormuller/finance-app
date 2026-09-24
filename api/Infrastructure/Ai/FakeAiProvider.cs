using Finance.Api.Application.Ai;

namespace Finance.Api.Infrastructure.Ai;

/// <summary>
/// E2E only, behind <c>Ai:FakeProvider</c>: a fixed answer, no network.
/// </summary>
public sealed class FakeAiProvider : IAiProvider
{
    public Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct) => throw new NotImplementedException();
}
