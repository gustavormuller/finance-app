using System.Globalization;
using Finance.Api.Domain.Transactions;

namespace Finance.Api.Endpoints;

/// <summary>
/// Which transactions the list selects, and so which ones its export (spec 021) writes:
/// parsed and applied here, once, so the screen and the file cannot disagree.
/// </summary>
/// <remarks>
/// The query arrives as strings, so a malformed value is a 400 with a pt-BR message
/// naming the field, and an empty one is no filter. A well-formed id that names nothing
/// the caller owns is not an error: the query filter hides it and nothing matches.
/// </remarks>
internal sealed record TransactionFilter(
    DateOnly? From,
    DateOnly? To,
    Guid? AccountId,
    Guid? CategoryId,
    Guid? ImportBatchId)
{
    private const string FromMessage = "A data inicial deve estar no formato AAAA-MM-DD, por exemplo 2026-01-31.";

    private const string ToMessage = "A data final deve estar no formato AAAA-MM-DD, por exemplo 2026-01-31.";

    public static (TransactionFilter? Filter, IResult? Problem) Parse(
        string? from,
        string? to,
        string? accountId,
        string? categoryId,
        string? importBatchId)
    {
        var start = ParseDate(from, out var badFrom);
        var end = ParseDate(to, out var badTo);
        var account = ParseId(accountId, out var badAccount);
        var category = ParseId(categoryId, out var badCategory);
        var batch = ParseId(importBatchId, out var badBatch);

        var problem = Problems.Validation(
            badFrom ? new RuleViolation("from", FromMessage) : null,
            badTo ? new RuleViolation("to", ToMessage) : null,
            badAccount ? new RuleViolation("accountId", "A conta do filtro não é um identificador válido.") : null,
            badCategory ? new RuleViolation("categoryId", "A categoria do filtro não é um identificador válido.") : null,
            badBatch ? new RuleViolation("importBatchId", "A importação do filtro não é um identificador válido.") : null);

        return problem is null ? (new TransactionFilter(start, end, account, category, batch), null) : (null, problem);
    }

    public IQueryable<Transaction> Apply(IQueryable<Transaction> query)
    {
        // Both ends inclusive, which is what a person means by "March 10th to March
        // 20th" on a filter bar.
        if (From is { } start)
        {
            query = query.Where(transaction => transaction.Date >= start);
        }

        if (To is { } end)
        {
            query = query.Where(transaction => transaction.Date <= end);
        }

        if (AccountId is { } onlyAccount)
        {
            query = query.Where(transaction => transaction.AccountId == onlyAccount);
        }

        // That category only, not its children, as the list has always filtered.
        if (CategoryId is { } onlyCategory)
        {
            query = query.Where(transaction => transaction.CategoryId == onlyCategory);
        }

        // 004: the "see what this import wrote" link from the done step.
        if (ImportBatchId is { } onlyBatch)
        {
            query = query.Where(transaction => transaction.ImportBatchId == onlyBatch);
        }

        return query;
    }

    private static DateOnly? ParseDate(string? text, out bool malformed)
    {
        malformed = false;

        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (DateOnly.TryParseExact(text, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        malformed = true;
        return null;
    }

    private static Guid? ParseId(string? text, out bool malformed)
    {
        malformed = false;

        if (string.IsNullOrEmpty(text))
        {
            return null;
        }

        if (Guid.TryParse(text, out var id))
        {
            return id;
        }

        malformed = true;
        return null;
    }
}
