using Finance.Api.Application.MarketData;
using Finance.Api.Domain.MarketData;
using Microsoft.Extensions.Options;

namespace Finance.Api.Infrastructure.MarketData;

public sealed class TwelveDataProvider(HttpClient http, IOptions<MarketDataOptions> options) : IPriceProvider
{
    public ProviderKind Kind => ProviderKind.TwelveData;

    public Task<IReadOnlyList<DailyClose>> GetDailyClosesAsync(string providerSymbol, DateOnly from, DateOnly to, CancellationToken ct) =>
        throw new NotImplementedException($"{http.BaseAddress}{options.Value.TwelveData.BaseUrl}");
}
