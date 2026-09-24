using System.Globalization;
using System.Text.RegularExpressions;
using Finance.Api.Application;
using Finance.Api.Application.Dashboard;
using Finance.Api.Domain.Transactions;

namespace Finance.Api.Endpoints;

/// <summary>
/// 005's three read-only dashboard routes. Parameters are bound as strings and
/// parsed here, so a malformed one is a 400 with a pt-BR message naming the field
/// rather than the framework's English binding failure.
/// </summary>
public static partial class DashboardEndpoints
{
    /// <summary>
    /// The spec's ceiling. Clamped rather than refused, like the transactions page
    /// size; below one is clamped up to one for the same reason.
    /// </summary>
    private const int MaximumMonths = 36;

    private const int DefaultMonths = 12;

    private const string MonthMessage = "O mês deve estar no formato AAAA-MM, por exemplo 2026-09.";

    private const string MonthsMessage = "O número de meses deve ser um número inteiro.";

    private const string KindMessage = "O tipo deve ser receita (Income) ou despesa (Expense).";

    private const string TransferMessage =
        "O tipo deve ser receita (Income) ou despesa (Expense): transferências entre as suas "
        + "contas não são receita nem despesa.";

    public static IEndpointRouteBuilder MapDashboardEndpoints(this IEndpointRouteBuilder routes)
    {
        var dashboard = routes.MapGroup("/api/dashboard").RequireAuthorization();

        dashboard.MapGet("/summary", async (
            DashboardQueries queries,
            ICurrentUser currentUser,
            CancellationToken cancellationToken,
            string? month = null) =>
        {
            if (ParseMonth(month) is not { } first)
            {
                return Problems.Validation("month", MonthMessage);
            }

            return Results.Ok(await queries.SummaryAsync(currentUser.Id!.Value, first, cancellationToken));
        });

        dashboard.MapGet("/monthly", async (
            DashboardQueries queries,
            ICurrentUser currentUser,
            CancellationToken cancellationToken,
            string? months = null) =>
        {
            var count = DefaultMonths;

            if (months is not null
                && !int.TryParse(months, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out count))
            {
                return Problems.Validation("months", MonthsMessage);
            }

            return Results.Ok(await queries.MonthlyAsync(
                currentUser.Id!.Value,
                CurrentMonth(),
                Math.Clamp(count, 1, MaximumMonths),
                cancellationToken));
        });

        dashboard.MapGet("/by-category", async (
            DashboardQueries queries,
            ICurrentUser currentUser,
            CancellationToken cancellationToken,
            string? month = null,
            string? kind = null) =>
        {
            var first = ParseMonth(month);
            var parsed = ParseKind(kind);

            if (Problems.Validation(
                    first is null ? new RuleViolation("month", MonthMessage) : null,
                    parsed.Problem is { } message ? new RuleViolation("kind", message) : null)
                is { } invalid)
            {
                return invalid;
            }

            return Results.Ok(await queries.ByCategoryAsync(
                currentUser.Id!.Value,
                first!.Value,
                parsed.Kind,
                cancellationToken));
        });

        return routes;
    }

    /// <summary>
    /// The calendar month by the UTC clock, the same one the transaction date rule
    /// uses. Near midnight at the turn of a month this is a few hours off Brasília,
    /// which a month selector corrects.
    /// </summary>
    private static DateOnly CurrentMonth()
    {
        var today = DateOnly.FromDateTime(DateTime.UtcNow);

        return new DateOnly(today.Year, today.Month, 1);
    }

    /// <summary>The first day of <c>YYYY-MM</c>, the current month when absent, null when malformed.</summary>
    private static DateOnly? ParseMonth(string? month)
    {
        if (month is null)
        {
            return CurrentMonth();
        }

        return MonthFormat().IsMatch(month)
            && DateOnly.TryParseExact(
                month + "-01",
                "yyyy-MM-dd",
                CultureInfo.InvariantCulture,
                DateTimeStyles.None,
                out var first)
            ? first
            : null;
    }

    /// <summary>
    /// <c>Income</c> or <c>Expense</c> by name, <c>Expense</c> when absent. Numbers are
    /// refused even though <see cref="Enum.TryParse{TEnum}(string, out TEnum)"/> would take
    /// them: <c>kind=2</c> is Transfer by another name.
    /// </summary>
    private static (CategoryKind Kind, string? Problem) ParseKind(string? kind) =>
        kind switch
        {
            null => (CategoryKind.Expense, null),
            _ when kind.Equals(nameof(CategoryKind.Expense), StringComparison.OrdinalIgnoreCase) =>
                (CategoryKind.Expense, null),
            _ when kind.Equals(nameof(CategoryKind.Income), StringComparison.OrdinalIgnoreCase) =>
                (CategoryKind.Income, null),
            _ when kind.Equals(nameof(CategoryKind.Transfer), StringComparison.OrdinalIgnoreCase)
                || kind == ((int)CategoryKind.Transfer).ToString(CultureInfo.InvariantCulture) =>
                (default, TransferMessage),
            _ => (default, KindMessage),
        };

    [GeneratedRegex(@"^[0-9]{4}-[0-9]{2}$")]
    private static partial Regex MonthFormat();
}
