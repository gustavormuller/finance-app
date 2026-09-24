using System.Globalization;
using Finance.Api.Application.Returns;
using Finance.Api.Domain.Transactions;

namespace Finance.Api.Endpoints;

/// <summary>
/// 008's routes under <c>/api/returns</c>. The query is bound as strings and parsed here,
/// so a malformed one is a 400 with a pt-BR message naming the field, as 005's dashboard
/// does. The numbers come from <see cref="ReturnsQueries"/>.
/// </summary>
public static class ReturnsEndpoints
{
    private const string DateFormat = "yyyy-MM-dd";

    private const string PeriodMessage =
        "O período deve ser desde o início (inception), no ano (ytd), 12 meses (12m) ou personalizado (custom).";

    private const string DatesWithPeriodMessage =
        "As datas de início e fim valem apenas para o período personalizado (custom).";

    private const string FromMessage = "A data inicial deve estar no formato AAAA-MM-DD, por exemplo 2026-01-31.";

    private const string ToMessage = "A data final deve estar no formato AAAA-MM-DD, por exemplo 2026-01-31.";

    private const string OrderMessage = "A data inicial deve ser anterior ou igual à data final.";

    public static IEndpointRouteBuilder MapReturnsEndpoints(this IEndpointRouteBuilder routes)
    {
        var returns = routes.MapGroup("/api/returns").RequireAuthorization();

        returns.MapGet("/portfolio", async (
            ReturnsQueries queries,
            CancellationToken cancellationToken,
            string? period = null,
            string? from = null,
            string? to = null) =>
        {
            var (query, problem) = Parse(period, from, to);
            return query is null ? problem! : Results.Ok(await queries.PortfolioAsync(query, cancellationToken));
        });

        // Somebody else's asset is filtered out, so it is a 404 like one that does not exist.
        returns.MapGet("/assets/{id:guid}", async (
            Guid id,
            ReturnsQueries queries,
            CancellationToken cancellationToken,
            string? period = null,
            string? from = null,
            string? to = null) =>
        {
            var (query, problem) = Parse(period, from, to);
            if (query is null)
            {
                return problem!;
            }

            return await queries.AssetAsync(id, query, cancellationToken) is { } view ? Results.Ok(view) : Results.NotFound();
        });

        return routes;
    }

    /// <summary>
    /// The query, or the 400 listing every field that is wrong. Dates alone mean a custom
    /// period; dates beside another period are refused rather than silently ignored.
    /// </summary>
    private static (ReturnsQuery? Query, IResult? Problem) Parse(string? period, string? from, string? to)
    {
        var start = ParseDate(from, out var badFrom);
        var end = ParseDate(to, out var badTo);
        var kind = period?.ToLowerInvariant() switch
        {
            null => from is null && to is null ? ReturnsPeriodKind.Inception : ReturnsPeriodKind.Custom,
            "inception" => ReturnsPeriodKind.Inception,
            "ytd" => ReturnsPeriodKind.Ytd,
            "12m" => ReturnsPeriodKind.TwelveMonths,
            "custom" => ReturnsPeriodKind.Custom,
            _ => (ReturnsPeriodKind?)null,
        };

        var problem = Problems.Validation(
            kind is null ? new RuleViolation("period", PeriodMessage)
            : kind != ReturnsPeriodKind.Custom && (from is not null || to is not null) ? new RuleViolation("period", DatesWithPeriodMessage)
            : null,
            badFrom ? new RuleViolation("from", FromMessage)
            : start > end ? new RuleViolation("from", OrderMessage)
            : null,
            badTo ? new RuleViolation("to", ToMessage) : null);

        return problem is null ? (new ReturnsQuery(kind!.Value, start, end), null) : (null, problem);
    }

    private static DateOnly? ParseDate(string? value, out bool malformed)
    {
        malformed = false;
        if (value is null)
        {
            return null;
        }

        if (DateOnly.TryParseExact(value, DateFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var date))
        {
            return date;
        }

        malformed = true;
        return null;
    }
}
