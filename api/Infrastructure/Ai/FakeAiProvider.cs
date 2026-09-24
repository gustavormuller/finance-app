using Finance.Api.Application.Ai;

namespace Finance.Api.Infrastructure.Ai;

/// <summary>
/// E2E only, behind <c>Ai:FakeProvider</c> (refused outside Development): a fixed pt-BR
/// answer and no network. Tokens are four characters each, rounded up, so the budget and
/// the usage rows see numbers that follow the request.
/// </summary>
/// <remarks>
/// One answer for every purpose. The categorisation (CP4) and E2E (CP7) checkpoints give it
/// the shapes their tests need.
/// </remarks>
public sealed class FakeAiProvider : IAiProvider
{
    /// <summary>A categorisation row whose description holds this gets a garbage answer (spec test 20).</summary>
    public const string GarbageMarker = "GARBAGE";

    private const string Answer =
        "## Resumo\n\nResposta de teste do provedor simulado. Nenhum dado saiu do servidor.";

    public Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        return Task.FromResult(new AiCompletion(Answer, Tokens(request.System.Length + request.User.Length), Tokens(Answer.Length)));
    }

    private static int Tokens(int characters) => (characters + 3) / 4;
}
