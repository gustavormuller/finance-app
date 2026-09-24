using System.Text.Encodings.Web;
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
    public const string System =
        "You categorise bank statement lines for a personal finance app in Brazil. " +
        "The user message is a JSON document with \"categories\" (id, name, kind) and \"rows\" " +
        "(rowId, description, kind). Pick, for each row, the one category that best fits its description. " +
        "A row's kind is the kind its category must have; a Transfer category, money moving between " +
        "the user's own accounts, fits either kind. Leave a row out when no category fits better than a " +
        "generic one. Answer with one JSON object mapping each rowId to a category id, for example " +
        "{\"<rowId>\": \"<category id>\"}, and nothing else: no prose, no code fence.";

    /// <summary>
    /// The answer's ceiling for <paramref name="rowCount"/> rows: about 64 tokens a pair (two
    /// GUIDs and their punctuation) and 256 for the frame. An answer cut at the ceiling fails
    /// in the adapter, so the ceiling errs high; only the tokens used are billed.
    /// </summary>
    public static int MaxTokensFor(int rowCount)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(rowCount, 1);
        ArgumentOutOfRangeException.ThrowIfGreaterThan(rowCount, BatchSize);
        return FrameTokens + (TokensPerRow * rowCount);
    }

    /// <summary>
    /// The user message: the rows and the categories, as one JSON document. A row goes with
    /// its normalized description and the kind its sign allows, never its amount.
    /// </summary>
    public static string BuildRequest(IReadOnlyCollection<AiCategorisationRow> rows, IReadOnlyCollection<AiCategoryOption> categories) =>
        JsonSerializer.Serialize(
            new
            {
                categories = categories.Select(category => new { id = category.Id, name = category.Name, kind = category.Kind.ToString() }),
                rows = rows.Select(row => new
                {
                    rowId = row.RowId,
                    description = row.Description,
                    kind = (row.Amount < 0m ? CategoryKind.Expense : CategoryKind.Income).ToString(),
                }),
            },
            Unescaped);

    private const int FrameTokens = 256;

    private const int TokensPerRow = 64;

    // Accents as they are: fewer tokens than \u escapes. The text goes to the model, never into HTML.
    private static readonly JsonSerializerOptions Unescaped = new() { Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping };

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
