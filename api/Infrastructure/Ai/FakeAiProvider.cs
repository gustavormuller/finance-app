using System.Text.Json;
using Finance.Api.Application.Ai;
using Finance.Api.Domain.Import;
using Finance.Api.Domain.Transactions;

namespace Finance.Api.Infrastructure.Ai;

/// <summary>
/// E2E only, behind <c>Ai:FakeProvider</c> (refused outside Development): no network, and
/// the same answer to the same request. Tokens are four characters each, rounded up, so the
/// budget and the usage rows see numbers that follow the request.
/// </summary>
/// <remarks>
/// A categorisation request (rung 3's system prompt) is answered as a model would, with a
/// <c>{ rowId: categoryId }</c> object: each row gets the first category of its kind, in the
/// order sent, that is not a sign default. A row holding <see cref="GarbageMarker"/> turns
/// the whole answer into the fixed markdown instead (spec test 20). Anything else gets the
/// fixed pt-BR markdown. CP7 shapes the analysis for E2E.
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
        var text = request.System == AiCategorisation.System ? Categorise(request.User) ?? Answer : Answer;
        return Task.FromResult(new AiCompletion(text, Tokens(request.System.Length + request.User.Length), Tokens(text.Length)));
    }

    private static string? Categorise(string user)
    {
        using var sent = JsonDocument.Parse(user);
        var rows = sent.RootElement.GetProperty("rows").EnumerateArray()
            .Select(row => (Id: row.GetProperty("rowId").GetString()!, Description: row.GetProperty("description").GetString()!, Kind: row.GetProperty("kind").GetString()!))
            .ToList();
        if (rows.Any(row => row.Description.Contains(GarbageMarker, StringComparison.Ordinal)))
        {
            return null;
        }

        var categories = sent.RootElement.GetProperty("categories").EnumerateArray()
            .Select(category => (Id: category.GetProperty("id").GetString()!, Name: category.GetProperty("name").GetString()!, Kind: category.GetProperty("kind").GetString()!))
            .Where(category => category.Name is not (DefaultCategories.OtherExpenseName or DefaultCategories.OtherIncomeName))
            .ToList();
        var answer = new Dictionary<string, string>();
        foreach (var row in rows)
        {
            if (categories.FirstOrDefault(category => category.Kind == row.Kind) is { Id: not null } chosen)
            {
                answer[row.Id] = chosen.Id;
            }
        }

        return JsonSerializer.Serialize(answer);
    }

    private static int Tokens(int characters) => (characters + 3) / 4;
}
