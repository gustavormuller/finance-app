using Finance.Api.Application.MarketData;
using Finance.Api.Domain.MarketData;
using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure;
using Finance.Api.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Finance.Api.Endpoints;

/// <summary>
/// 006's market-data routes: the shared catalogue, its price series, the benchmark
/// series. Every signed-in user reads and registers into the same catalogue; none of
/// it is user-owned, so there is no query filter to go through.
/// </summary>
public static class MarketDataEndpoints
{
    /// <summary>A search is a picker, not an export.</summary>
    private const int SearchLimit = 50;

    private static readonly string[] Currencies = ["BRL", "USD"];

    /// <remarks>
    /// <c>Name</c> is not in the spec's body, but the column is required: when omitted,
    /// the ticker stands in for it.
    /// </remarks>
    private sealed record AssetRequest(
        string? Ticker,
        string? Name,
        MarketAssetClass Class,
        ProviderKind Provider,
        string? ProviderSymbol,
        string? Currency);

    private sealed record AssetResponse(
        Guid Id,
        string Ticker,
        string Name,
        MarketAssetClass Class,
        string Currency,
        ProviderKind Provider,
        string ProviderSymbol,
        bool IsActive,
        DateTimeOffset? LastSyncedAt,
        DateTimeOffset CreatedAt);

    private sealed record PriceResponse(DateOnly Date, decimal Close);

    private sealed record BenchmarkResponse(DateOnly Date, decimal Value);

    private sealed record SyncAccepted(Guid SyncRunId);

    /// <summary><c>Summary</c> parsed into the sync's own shape, so the screen reads an object, not a string.</summary>
    private sealed record SyncRunResponse(
        Guid Id,
        DateTimeOffset StartedAt,
        DateTimeOffset? FinishedAt,
        SyncTrigger Trigger,
        SyncRunStatus Status,
        Dictionary<string, ProviderSyncSummary> Summary);

    /// <summary>The run history the spec asks for.</summary>
    private const int SyncRunLimit = 20;

    public static IEndpointRouteBuilder MapMarketDataEndpoints(this IEndpointRouteBuilder routes)
    {
        var marketData = routes.MapGroup("/api/market-data").RequireAuthorization();

        marketData.MapGet("/assets", async (AppDbContext database, CancellationToken cancellationToken, string? q = null) =>
        {
            var query = database.Set<MarketAsset>().AsNoTracking();
            if (!string.IsNullOrWhiteSpace(q))
            {
                var pattern = "%" + EscapeLike(q.Trim()) + "%";
                query = query.Where(asset =>
                    EF.Functions.ILike(asset.Ticker, pattern, "\\") || EF.Functions.ILike(asset.Name, pattern, "\\"));
            }

            return Results.Ok((await query.OrderBy(asset => asset.Ticker).ThenBy(asset => asset.Provider)
                .Take(SearchLimit).ToListAsync(cancellationToken)).Select(Describe));
        });

        marketData.MapPost("/assets", async (
            AssetRequest request,
            AppDbContext database,
            IOptions<MarketDataOptions> options,
            TimeProvider clock,
            CancellationToken cancellationToken) =>
        {
            if (Validate(request, options.Value) is { } invalid)
            {
                return invalid;
            }

            var ticker = request.Ticker!.Trim().ToUpperInvariant();
            var asset = new MarketAsset
            {
                Id = Guid.NewGuid(),
                Ticker = ticker,
                Name = string.IsNullOrWhiteSpace(request.Name) ? ticker : request.Name.Trim(),
                Class = request.Class,
                Currency = request.Currency!.Trim().ToUpperInvariant(),
                Provider = request.Provider,
                ProviderSymbol = request.ProviderSymbol!.Trim(),
                IsActive = true,
                CreatedAt = clock.GetUtcNow(),
            };
            database.Add(asset);

            try
            {
                await database.SaveChangesAsync(cancellationToken);
            }
            catch (DbUpdateException exception) when (exception.IsDuplicate())
            {
                return Problems.Conflict(
                    $"O símbolo '{asset.ProviderSymbol}' já está cadastrado no provedor {asset.Provider}.");
            }

            return Results.Created($"/api/market-data/assets/{asset.Id}", Describe(asset));
        });

        marketData.MapGet("/assets/{id:guid}/prices", async (
            Guid id,
            AppDbContext database,
            CancellationToken cancellationToken,
            DateOnly? from = null,
            DateOnly? to = null) =>
        {
            if (!await database.Set<MarketAsset>().AnyAsync(asset => asset.Id == id, cancellationToken))
            {
                return Results.NotFound();
            }

            var query = database.Set<Price>().AsNoTracking().Where(price => price.MarketAssetId == id);
            query = from is { } start ? query.Where(price => price.Date >= start) : query;
            query = to is { } end ? query.Where(price => price.Date <= end) : query;

            return Results.Ok(await query.OrderBy(price => price.Date)
                .Select(price => new PriceResponse(price.Date, price.Close)).ToListAsync(cancellationToken));
        });

        // An unknown code is an empty series, like a configured one not yet synced.
        marketData.MapGet("/benchmarks/{code}", async (
            string code,
            AppDbContext database,
            CancellationToken cancellationToken,
            DateOnly? from = null,
            DateOnly? to = null) =>
        {
            var normalized = code.Trim().ToUpperInvariant();
            var query = database.Set<Benchmark>().AsNoTracking().Where(value => value.Code == normalized);
            query = from is { } start ? query.Where(value => value.Date >= start) : query;
            query = to is { } end ? query.Where(value => value.Date <= end) : query;

            return Results.Ok(await query.OrderBy(value => value.Date)
                .Select(value => new BenchmarkResponse(value.Date, value.Value)).ToListAsync(cancellationToken));
        });

        marketData.MapPost("/sync", async (ManualMarketDataSync sync, HttpContext context, CancellationToken cancellationToken) =>
        {
            var started = await sync.StartAsync(cancellationToken);
            if (started.SyncRunId is not { } syncRunId)
            {
                var minutes = (int)Math.Ceiling(started.RetryAfter.TotalMinutes);
                return Problems.TooManyRequests(
                    context,
                    "Uma sincronização foi iniciada há menos de 10 minutos ou ainda está em andamento. "
                    + $"Tente novamente em {minutes} {(minutes == 1 ? "minuto" : "minutos")}.",
                    started.RetryAfter);
            }

            return Results.Accepted(value: new SyncAccepted(syncRunId));
        });

        marketData.MapGet("/sync-runs", async (AppDbContext database, CancellationToken cancellationToken) =>
            Results.Ok((await database.Set<SyncRun>().AsNoTracking()
                    .OrderByDescending(run => run.StartedAt).Take(SyncRunLimit).ToListAsync(cancellationToken))
                .Select(run => new SyncRunResponse(
                    run.Id, run.StartedAt, run.FinishedAt, run.Trigger, run.Status, SyncSummaryJson.Read(run.Summary)))));

        return routes;
    }

    private static IResult? Validate(AssetRequest request, MarketDataOptions options)
    {
        var currency = request.Currency?.Trim().ToUpperInvariant();
        return Problems.Validation(
            string.IsNullOrWhiteSpace(request.Ticker) || request.Ticker.Trim().Length > 20
                ? new RuleViolation("ticker", "O ticker é obrigatório e deve ter até 20 caracteres.")
                : null,
            request.Name is { } name && name.Trim().Length > 200
                ? new RuleViolation("name", "O nome deve ter até 200 caracteres.")
                : null,
            !Enum.IsDefined(request.Class)
                ? new RuleViolation("class", "A classe do ativo não é válida.")
                : null,
            !Enum.IsDefined(request.Provider)
                ? new RuleViolation("provider", "O provedor não é válido.")
                : null,
            string.IsNullOrWhiteSpace(request.ProviderSymbol) || request.ProviderSymbol.Trim().Length > 50
                ? new RuleViolation("providerSymbol", "O símbolo no provedor é obrigatório e deve ter até 50 caracteres.")
                : null,
            currency is null || !Currencies.Contains(currency)
                ? new RuleViolation("currency", "A moeda deve ser BRL ou USD.")
                : request.Provider == ProviderKind.CoinGecko
                    && !string.Equals(currency, options.CoinGecko.VsCurrency, StringComparison.OrdinalIgnoreCase)
                    ? new RuleViolation(
                        "currency",
                        $"Ativos do CoinGecko são cotados em {options.CoinGecko.VsCurrency.ToUpperInvariant()}.")
                    : null);
    }

    private static string EscapeLike(string text) =>
        text.Replace("\\", "\\\\").Replace("%", "\\%").Replace("_", "\\_");

    private static AssetResponse Describe(MarketAsset asset) =>
        new(asset.Id, asset.Ticker, asset.Name, asset.Class, asset.Currency, asset.Provider,
            asset.ProviderSymbol, asset.IsActive, asset.LastSyncedAt, asset.CreatedAt);
}
