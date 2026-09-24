using System.Text.Json;
using Finance.Api.Domain.Transactions;

namespace Finance.Api.Domain.Import;

/// <summary>A row sent to the model: its id, its normalized description and its signed amount, which is not sent.</summary>
public sealed record AiCategorisationRow(Guid RowId, string Description, decimal Amount);

/// <summary>A category the model may answer with, by id, with the name the user sees and its kind.</summary>
public sealed record AiCategoryOption(Guid Id, string Name, CategoryKind Kind);

/// <summary>
/// ADR-012 rung 3, the pure half: what is sent to the model, how many tokens it may answer
/// with, and what of its answer is kept (009, "CategorisationCascade — rung 3").
/// </summary>
public static class AiCategorisation
{
    /// <summary>Rows per provider call. Each call is budgeted, timed out and recorded on its own.</summary>
    public const int BatchSize = 40;

    /// <summary>The instructions sent as the system prompt. Not shown to anyone, so English.</summary>
    public const string System = "";

    /// <summary>The answer's ceiling for <paramref name="rowCount"/> rows.</summary>
    public static int MaxTokensFor(int rowCount) => throw new NotImplementedException();

    /// <summary>The user message: the rows and the categories, as one JSON document.</summary>
    public static string BuildRequest(IReadOnlyCollection<AiCategorisationRow> rows, IReadOnlyCollection<AiCategoryOption> categories) =>
        throw new NotImplementedException();

    /// <summary>
    /// The model's answer, read as untrusted text: a <c>{ rowId: categoryId }</c> object,
    /// kept only where the row was sent, the category is one of the user's, and its kind
    /// agrees with the row's sign (003, rule 3). Anything else is dropped, never thrown.
    /// </summary>
    /// <param name="rows">The rows sent, by id, with their signed amounts.</param>
    /// <param name="categories">The user's categories.</param>
    public static IReadOnlyDictionary<Guid, Guid> Parse(
        string? text,
        IReadOnlyDictionary<Guid, decimal> rows,
        IReadOnlyCollection<CategoryChoice> categories)
    {
        var kept = new Dictionary<Guid, Guid>();

        using var answer = string.IsNullOrWhiteSpace(text) ? null : ReadObject(text);
        if (answer is null)
        {
            return kept;
        }

        var owned = categories.ToDictionary(category => category.Id, category => category.Kind);

        foreach (var property in answer.RootElement.EnumerateObject())
        {
            if (Guid.TryParse(property.Name, out var rowId)
                && !kept.ContainsKey(rowId)
                && rows.TryGetValue(rowId, out var amount)
                && amount != 0m
                && property.Value.ValueKind == JsonValueKind.String
                && Guid.TryParse(property.Value.GetString(), out var categoryId)
                && owned.TryGetValue(categoryId, out var kind)
                && TransactionRules.ValidateSign(amount, kind) is null)
            {
                kept[rowId] = categoryId;
            }
        }

        return kept;
    }

    private static readonly JsonDocumentOptions Lenient = new()
    {
        AllowTrailingCommas = true,
        CommentHandling = JsonCommentHandling.Skip,
    };

    /// <summary>
    /// The first reading that is a JSON object: the text as it is, the inside of a code
    /// fence, or the span from the first <c>{</c> to the last <c>}</c> (prose around it).
    /// </summary>
    private static JsonDocument? ReadObject(string text)
    {
        foreach (var candidate in Candidates(text))
        {
            try
            {
                var document = JsonDocument.Parse(candidate, Lenient);
                if (document.RootElement.ValueKind == JsonValueKind.Object)
                {
                    return document;
                }

                document.Dispose();
            }
            catch (JsonException)
            {
                // Not this reading; try the next.
            }
        }

        return null;
    }

    private static IEnumerable<string> Candidates(string text)
    {
        yield return text.Trim();

        var fence = text.IndexOf("```", StringComparison.Ordinal);
        if (fence >= 0)
        {
            var bodyStart = text.IndexOf('\n', fence);
            var close = bodyStart < 0 ? -1 : text.IndexOf("```", bodyStart, StringComparison.Ordinal);
            if (close > bodyStart)
            {
                yield return text[bodyStart..close].Trim();
            }
        }

        var open = text.IndexOf('{');
        var end = text.LastIndexOf('}');
        if (open >= 0 && end > open)
        {
            yield return text[open..(end + 1)];
        }
    }
}
