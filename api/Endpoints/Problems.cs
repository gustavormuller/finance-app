using Finance.Api.Application.Ai;
using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Finance.Api.Endpoints;

/// <summary>
/// The failure shapes the endpoints answer with, in one place so every route spells
/// them the same way.
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
    /// A refusal that resolves itself with time. <c>Retry-After</c> says how long, in
    /// whole seconds, rounded up.
    /// </summary>
    public static IResult TooManyRequests(HttpContext context, string reason, TimeSpan retryAfter)
    {
        context.Response.Headers.RetryAfter =
            ((int)Math.Ceiling(retryAfter.TotalSeconds)).ToString(System.Globalization.CultureInfo.InvariantCulture);

        return Results.Problem(
            title: "Muitas solicitações",
            detail: reason,
            statusCode: StatusCodes.Status429TooManyRequests);
    }

    /// <summary>Whether <see cref="Ai"/> has an answer for this failure.</summary>
    public static bool IsAiFailure(Exception exception) =>
        exception is AiDisabledException or AiBudgetExceededException or AiProviderException;

    /// <summary>
    /// An AI gate or provider failure as the pt-BR problem its status stands for: 403 AI
    /// off, 402 budget spent, 504 timed out, 502 any other provider failure. Never the
    /// exception's message, which is English and may name the model.
    /// </summary>
    public static IResult Ai(Exception exception) => exception switch
    {
        AiDisabledException => Results.Problem(
            title: "IA desligada",
            detail: AiFailureText.Disabled,
            statusCode: StatusCodes.Status403Forbidden),
        AiBudgetExceededException => Results.Problem(
            title: "Limite de IA atingido",
            detail: AiFailureText.BudgetExceeded,
            statusCode: StatusCodes.Status402PaymentRequired),
        AiProviderTimeoutException => Results.Problem(
            title: "A IA demorou demais",
            detail: AiFailureText.Timeout,
            statusCode: StatusCodes.Status504GatewayTimeout),
        AiProviderException => Results.Problem(
            title: "Falha no serviço de IA",
            detail: AiFailureText.ProviderFailed,
            statusCode: StatusCodes.Status502BadGateway),
        _ => throw new ArgumentException("Not an AI failure.", nameof(exception), exception),
    };

    /// <summary>
    /// Turns a unique-index violation into the 409 it is. Caught rather than
    /// pre-checked with a query, because a pre-check races with a concurrent insert
    /// and the index does not.
    /// </summary>
    public static bool IsDuplicate(this DbUpdateException exception) =>
        exception.InnerException is PostgresException { SqlState: UniqueViolation };

    /// <summary>Saves; false when a unique index refused the write (<see cref="IsDuplicate"/>).</summary>
    public static async Task<bool> TrySaveAsync(this AppDbContext database, CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateException exception) when (exception.IsDuplicate())
        {
            return false;
        }
    }
}
