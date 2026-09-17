using Finance.Api.Domain.Transactions;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Finance.Api.Endpoints;

/// <summary>
/// The two failure shapes 003's endpoints answer with, in one place so every route
/// spells them the same way.
/// </summary>
/// <remarks>
/// A rejected field is always a 400 naming the field, never a 403 — including when
/// the field names a row belonging to somebody else. A 403 would confirm the id
/// exists, which is the leak spec rule 4 exists to prevent.
/// </remarks>
internal static class Problems
{
    /// <summary>PostgreSQL's SQLSTATE for unique_violation.</summary>
    private const string UniqueViolation = "23505";

    public static IResult Validation(string field, string message) =>
        Results.ValidationProblem(new Dictionary<string, string[]> { [field] = [message] });

    /// <summary>
    /// Every rule that failed, reported at once. A form that fixes one field only to
    /// be told about the next is worse than one told everything up front.
    /// </summary>
    public static IResult? Validation(params ReadOnlySpan<RuleViolation?> violations)
    {
        var errors = new Dictionary<string, string[]>();

        foreach (var violation in violations)
        {
            if (violation is not { } failed)
            {
                continue;
            }

            errors[failed.Field] = errors.TryGetValue(failed.Field, out var existing)
                ? [.. existing, failed.Message]
                : [failed.Message];
        }

        return errors.Count == 0 ? null : Results.ValidationProblem(errors);
    }

    /// <summary>
    /// A refusal the caller could resolve by doing something else first — deleting
    /// the children, moving the transactions, picking another name. The reason is in
    /// the body because the spec asks the UI to show it rather than fail silently.
    /// </summary>
    public static IResult Conflict(string reason) =>
        Results.Problem(
            title: "Conflito",
            detail: reason,
            statusCode: StatusCodes.Status409Conflict);

    /// <summary>
    /// Turns a unique-index violation into the 409 it is. Caught rather than
    /// pre-checked with a query, because a pre-check races with a concurrent insert
    /// and the index does not.
    /// </summary>
    public static bool IsDuplicate(this DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: UniqueViolation };
}
