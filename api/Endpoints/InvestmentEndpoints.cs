using Finance.Api.Application;
using Finance.Api.Application.Investments;
using Finance.Api.Application.MarketData;
using Finance.Api.Domain.Investments;
using Finance.Api.Domain.MarketData;
using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Finance.Api.Endpoints;

/// <summary>
/// 007's routes under <c>/api/investments</c>. Every set read here is user-owned and
/// filtered (ADR-007), so somebody else's asset or movement is a 404, never a 403.
/// </summary>
public static class InvestmentEndpoints
{
    private const int NicknameLength = 100;

    /// <summary>
    /// Either <c>marketAssetId</c>, or a catalogue registration as 006's
    /// <c>POST /api/market-data/assets</c> takes it (spec: "also accepts").
    /// </summary>
    private sealed record AddAssetRequest(
        Guid? MarketAssetId,
        string? Nickname,
        string? Ticker,
        string? Name,
        MarketAssetClass Class,
        ProviderKind Provider,
        string? ProviderSymbol,
        string? Currency);

    private sealed record MovementResponse(
        Guid Id,
        Guid AssetId,
        DateOnly Date,
        MovementKind Kind,
        decimal Quantity,
        decimal UnitPrice,
        decimal Amount,
        decimal Fees,
        string Currency,
        string? Notes,
        DateTimeOffset CreatedAt);

    private sealed record DailyResponse(
        DateOnly Date,
        decimal Quantity,
        decimal AverageCost,
        decimal Price,
        DateOnly PriceDate,
        decimal FxRate,
        decimal ValueBrl,
        decimal CostBasisBrl);

    private sealed record RebuildResponse(int AssetsRebuilt, int RowsWritten);

    public static IEndpointRouteBuilder MapInvestmentEndpoints(this IEndpointRouteBuilder routes)
    {
        var investments = routes.MapGroup("/api/investments").RequireAuthorization();

        investments.MapGet("/assets", async (PositionQueries positions, CancellationToken cancellationToken) =>
            Results.Ok(await positions.ListAsync(cancellationToken)));

        investments.MapPost("/assets", async (
            AddAssetRequest request,
            AppDbContext database,
            ICurrentUser currentUser,
            PositionQueries positions,
            IOptions<MarketDataOptions> options,
            TimeProvider clock,
            CancellationToken cancellationToken) =>
        {
            var nickname = RequestText.Optional(request.Nickname);
            if (nickname is { Length: > NicknameLength })
            {
                return Problems.Validation("nickname", $"O apelido deve ter até {NicknameLength} caracteres.");
            }

            MarketAsset? market;
            var registered = false;
            if (request.MarketAssetId is { } marketAssetId)
            {
                market = await database.MarketAssets.SingleOrDefaultAsync(asset => asset.Id == marketAssetId, cancellationToken);
                if (market is null)
                {
                    return Problems.Validation("marketAssetId", "Ativo não encontrado no catálogo.");
                }
            }
            else
            {
                var catalogue = new MarketDataEndpoints.AssetRequest(
                    request.Ticker, request.Name, request.Class, request.Provider, request.ProviderSymbol, request.Currency);
                if (MarketDataEndpoints.Validate(catalogue, options.Value) is { } invalid)
                {
                    return invalid;
                }

                // An entry already in the catalogue under the same provider and symbol is
                // the one held; the request's other fields do not overwrite it.
                var candidate = MarketDataEndpoints.NewAsset(catalogue, clock);
                market = await database.MarketAssets.SingleOrDefaultAsync(
                    asset => asset.Provider == candidate.Provider && asset.ProviderSymbol == candidate.ProviderSymbol,
                    cancellationToken);
                if (market is null)
                {
                    market = candidate;
                    database.Add(market);
                    registered = true;
                }
            }

            var held = new Asset
            {
                Id = Guid.NewGuid(),
                UserId = currentUser.Id!.Value,
                MarketAssetId = market.Id,
                Nickname = nickname,
                CreatedAt = clock.GetUtcNow(),
            };
            database.Add(held);

            if (!await database.TrySaveAsync(cancellationToken))
            {
                // A new catalogue row cannot already be held, so its duplicate is the
                // catalogue's: another request registered the same symbol first.
                return registered
                    ? MarketDataEndpoints.DuplicateSymbol(market)
                    : Problems.Conflict("Você já possui este ativo na carteira.");
            }

            return Results.Created(
                $"/api/investments/assets/{held.Id}",
                (await positions.ListAsync(cancellationToken, held.Id)).Single());
        });

        investments.MapDelete("/assets/{id:guid}", async (Guid id, AppDbContext database, CancellationToken cancellationToken) =>
        {
            var held = await database.Assets.SingleOrDefaultAsync(asset => asset.Id == id, cancellationToken);
            if (held is null)
            {
                return Results.NotFound();
            }

            // Movements are facts (ADR-006): they are removed one by one, never by implication.
            if (await database.Movements.AnyAsync(movement => movement.AssetId == id, cancellationToken))
            {
                return Problems.Conflict("O ativo tem movimentações. Exclua-as antes de remover o ativo.");
            }

            // Its daily rows go with it (CASCADE, ADR-011).
            database.Assets.Remove(held);
            await database.SaveChangesAsync(cancellationToken);
            return Results.NoContent();
        });

        // In replay order: by date, then by creation (PositionCalculator.InOrder).
        investments.MapGet("/assets/{id:guid}/movements", async (Guid id, AppDbContext database, CancellationToken cancellationToken) =>
            await database.Assets.AnyAsync(asset => asset.Id == id, cancellationToken)
                ? Results.Ok((await database.Movements.AsNoTracking().Where(movement => movement.AssetId == id)
                        .OrderBy(movement => movement.Date).ThenBy(movement => movement.CreatedAt).ToListAsync(cancellationToken))
                    .Select(Describe))
                : Results.NotFound());

        investments.MapPost("/assets/{id:guid}/movements", async (
            Guid id, MovementInput input, MovementCommands commands, CancellationToken cancellationToken) =>
            Answer(await commands.AddAsync(id, input, cancellationToken),
                movement => Results.Created($"/api/investments/movements/{movement.Id}", Describe(movement))));

        investments.MapPut("/movements/{id:guid}", async (
            Guid id, MovementInput input, MovementCommands commands, CancellationToken cancellationToken) =>
            Answer(await commands.UpdateAsync(id, input, cancellationToken), movement => Results.Ok(Describe(movement))));

        // A delete that would uncover a later sell has no field to blame: a 409 with the rule's text.
        investments.MapDelete("/movements/{id:guid}", async (Guid id, MovementCommands commands, CancellationToken cancellationToken) =>
            await commands.DeleteAsync(id, cancellationToken) switch
            {
                { Outcome: MovementOutcome.NotFound } => Results.NotFound(),
                { Outcome: MovementOutcome.Invalid, Violations: [var violation, ..] } => Problems.Conflict(violation.Message),
                _ => Results.NoContent(),
            });

        // Both ends inclusive, oldest first.
        investments.MapGet("/assets/{id:guid}/daily", async (
            Guid id,
            AppDbContext database,
            CancellationToken cancellationToken,
            DateOnly? from = null,
            DateOnly? to = null) =>
        {
            if (!await database.Assets.AnyAsync(asset => asset.Id == id, cancellationToken))
            {
                return Results.NotFound();
            }

            var rows = database.PortfolioDaily.AsNoTracking().Where(row => row.AssetId == id);
            rows = from is { } start ? rows.Where(row => row.Date >= start) : rows;
            rows = to is { } end ? rows.Where(row => row.Date <= end) : rows;
            return Results.Ok(await rows.OrderBy(row => row.Date)
                .Select(row => new DailyResponse(
                    row.Date, row.Quantity, row.AverageCost, row.Price, row.PriceDate, row.FxRate, row.ValueBrl, row.CostBasisBrl))
                .ToListAsync(cancellationToken));
        });

        investments.MapGet("/summary", async (PositionQueries positions, CancellationToken cancellationToken) =>
            Results.Ok(await positions.SummaryAsync(cancellationToken)));

        // 202 as the spec says, but the work is done when it answers: one user's assets
        // take well under a second each (decision 9), so there is nothing to poll.
        investments.MapPost("/rebuild", async (SnapshotRebuild rebuild, CancellationToken cancellationToken) =>
        {
            var (assets, rows) = await rebuild.RebuildAllAsync(cancellationToken);
            return Results.Accepted(value: new RebuildResponse(assets, rows));
        });

        return routes;
    }

    private static IResult Answer(MovementWrite write, Func<Movement, IResult> done) => write switch
    {
        { Outcome: MovementOutcome.NotFound } => Results.NotFound(),
        { Outcome: MovementOutcome.Invalid } => Problems.Validation(write.Violations!)!,
        _ => done(write.Movement!),
    };

    private static MovementResponse Describe(Movement movement) =>
        new(movement.Id, movement.AssetId, movement.Date, movement.Kind, movement.Quantity, movement.UnitPrice,
            movement.Amount, movement.Fees, movement.Currency, movement.Notes, movement.CreatedAt);
}
