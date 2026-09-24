using Finance.Api.Application.MarketData;
using Finance.Api.Domain.MarketData;
using Microsoft.Extensions.Options;

namespace Finance.Api.Infrastructure.MarketData;

public sealed class BrapiProvider(HttpClient http, IOptions<MarketDataOptions> options, TimeProvider clock) : IPriceProvider
{
    public ProviderKind Kind => ProviderKind.Brapi;

    public Task<IReadOnlyList<DailyClose>> GetDailyClosesAsync(string providerSymbol, DateOnly from, DateOnly to, CancellationToken ct) =>
        throw new NotImplementedException($"{http.BaseAddress}{options.Value.Brapi.BaseUrl}{clock.GetUtcNow()}");
}
