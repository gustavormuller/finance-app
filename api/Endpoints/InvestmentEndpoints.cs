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
            var nickname = string.IsNullOrWhiteSpace(request.Nickname) ? null : request.Nickname.Trim();
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

            try
            {
                await database.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.IsDuplicate())
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

        return routes;
    }
}
