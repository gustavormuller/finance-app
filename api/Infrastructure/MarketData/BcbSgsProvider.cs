using Finance.Api.Application.MarketData;
using Microsoft.Extensions.Options;

namespace Finance.Api.Infrastructure.MarketData;

public sealed class BcbSgsProvider(HttpClient http, IOptions<MarketDataOptions> options) : IBenchmarkProvider
{
    public Task<IReadOnlyList<DailyValue>> GetSeriesAsync(string code, DateOnly from, DateOnly to, CancellationToken ct) =>
        throw new NotImplementedException($"{http.BaseAddress}{options.Value.Bcb.BaseUrl}");
}
