using Finance.Api.Application.Ai;
using Microsoft.Extensions.Options;

namespace Finance.Api.Infrastructure.Ai;

public sealed class AnthropicAiProvider(HttpClient http, IOptions<AiOptions> options) : IAiProvider
{
    public Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct) =>
        throw new NotImplementedException($"{http}{options}");
}
