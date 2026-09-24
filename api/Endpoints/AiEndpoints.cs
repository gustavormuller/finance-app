using Finance.Api.Application;
using Finance.Api.Application.Ai;
using Finance.Api.Domain.Ai;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Finance.Api.Endpoints;

/// <summary>
/// 009's monthly analysis and the user's AI spend. Every read goes through the query filter,
/// so another user's analysis is a 404 and their usage is never summed.
/// </summary>
public static class AiEndpoints
{
    private const string MonthFormat = "Informe o mês no formato AAAA-MM.";

    private sealed record AnalysisRequest(string? Month);

    private sealed record AnalysisAccepted(Guid AnalysisId);

    private sealed record AnalysisResponse(
        Guid Id,
        string Month,
        AiAnalysisStatus Status,
        string? Content,
        string? Error,
        string PromptVersion,
        DateTimeOffset CreatedAt,
        DateTimeOffset? StartedAt,
        DateTimeOffset? CompletedAt);

    private sealed record UsageResponse(string Month, decimal SpentBrl, decimal BudgetBrl, int Calls);

    public static IEndpointRouteBuilder MapAiEndpoints(this IEndpointRouteBuilder routes)
    {
        var ai = routes.MapGroup("/api/ai").RequireAuthorization();

        // 202 with the row's id; the job runs it (decision 6) and the card polls GET /{id}.
        ai.MapPost("/analyses", async (
            AnalysisRequest? body, AnalysisCommands commands, TimeProvider clock, ILoggerFactory loggers, CancellationToken ct) =>
        {
            if (!AnalysisCommands.IsMonth(body?.Month))
            {
                return Problems.Validation("month", MonthFormat);
            }

            if (string.CompareOrdinal(body!.Month, AiCost.MonthOf(clock.GetUtcNow())) > 0)
            {
                return Problems.Validation("month", "Não é possível analisar um mês que ainda não começou.");
            }

            AnalysisRequestResult result;
            try
            {
                result = await commands.RequestAsync(body.Month!, ct);
            }
            catch (Exception failure) when (Problems.IsAiFailure(failure))
            {
                loggers.CreateLogger(nameof(AiEndpoints)).LogWarning(failure, "AI analysis for {Month} refused", body.Month);
                return Problems.Ai(failure);
            }
            catch (DbUpdateException duplicate) when (duplicate.IsDuplicate())
            {
                return InProgress();
            }

            return result.Problem == AnalysisRequestProblem.InProgress
                ? InProgress()
                : Results.Accepted($"/api/ai/analyses/{result.AnalysisId}", new AnalysisAccepted(result.AnalysisId));
        });

        ai.MapGet("/analyses/{id:guid}", async (Guid id, AppDbContext db, CancellationToken ct) =>
            await Describe(db.AiAnalyses.Where(row => row.Id == id)).SingleOrDefaultAsync(ct) is { } analysis
                ? Results.Ok(analysis)
                : Results.NotFound());

        ai.MapGet("/analyses", async (AppDbContext db, CancellationToken ct, string? month = null) =>
        {
            if (month is not null && !AnalysisCommands.IsMonth(month))
            {
                return Problems.Validation("month", MonthFormat);
            }

            var rows = db.AiAnalyses.Where(row => month == null || row.Month == month).OrderByDescending(row => row.Month);
            return Results.Ok(await Describe(rows).ToListAsync(ct));
        });

        // The month's spend against the budget, failed calls included. Open with AI off, so
        // /settings can show it; the current month (UTC-3, as the usage rows) by default.
        ai.MapGet("/usage", async (
            AppDbContext db, ICurrentUser user, BudgetGuard budget, IOptions<AiOptions> options, TimeProvider clock,
            CancellationToken ct, string? month = null) =>
        {
            if (month is not null && !AnalysisCommands.IsMonth(month))
            {
                return Problems.Validation("month", MonthFormat);
            }

            month ??= AiCost.MonthOf(clock.GetUtcNow());
            var spent = await budget.SpentAsync(user.Id!.Value, month, ct);
            var calls = await db.AiUsage.CountAsync(usage => usage.Month == month, ct);
            return Results.Ok(new UsageResponse(month, spent, options.Value.MonthlyBudgetBrl, calls));
        });

        return routes;
    }

    private static IResult InProgress() =>
        Problems.Conflict("A análise deste mês ainda está sendo gerada. Aguarde ela terminar para gerar outra.");

    private static IQueryable<AnalysisResponse> Describe(IQueryable<AiAnalysis> rows) =>
        rows.AsNoTracking().Select(row => new AnalysisResponse(
            row.Id, row.Month, row.Status, row.Content, row.Error, row.PromptVersion, row.CreatedAt, row.StartedAt, row.CompletedAt));
}
