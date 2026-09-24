using Finance.Api.Domain.Ai;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace Finance.Api.Application.Ai;

/// <summary>
/// Prices a call in BRL: the model's USD list prices from <c>Ai:Pricing</c>, at the latest
/// stored <c>USDBRL</c> (006) or <c>Ai:UsdBrl</c> when there is none.
/// </summary>
public sealed class AiPricing(AppDbContext db, IOptions<AiOptions> options)
{
    /// <summary>The <c>Benchmarks</c> code 006 stores the dollar under (BCB SGS series 1).</summary>
    public const string UsdBrlCode = "USDBRL";

    public async Task<decimal> CostBrlAsync(string model, int inputTokens, int outputTokens, CancellationToken ct)
    {
        // The boot refuses a configured model without a price, so this lookup cannot miss.
        var price = options.Value.Pricing[model];
        var latest = await db.Benchmarks
            .Where(benchmark => benchmark.Code == UsdBrlCode)
            .OrderByDescending(benchmark => benchmark.Date)
            .Select(benchmark => (decimal?)benchmark.Value)
            .FirstOrDefaultAsync(ct);

        return AiCost.Brl(
            inputTokens, outputTokens, price.InputPerMTokUsd, price.OutputPerMTokUsd, AiCost.UsdBrl(latest, options.Value.UsdBrl));
    }
}
