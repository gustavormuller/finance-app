using System.Globalization;
using System.Net;
using Finance.Api.Application.MarketData;
using Microsoft.Extensions.Options;

namespace Finance.Api.Infrastructure.MarketData;

/// <summary>
/// Banco Central's SGS: <c>{BaseUrl}{code}/dados?formato=json&amp;dataInicial=dd/MM/yyyy&amp;dataFinal=dd/MM/yyyy</c>,
/// answering <c>[{"data":"02/01/2024","valor":"0.043739"}]</c>. Benchmark codes map to
/// SGS numbers through <c>MarketData:Bcb:Series</c>; values stay in the series' unit.
/// </summary>
/// <remarks>
/// Dates and values are text: dates in Brazilian order, values with a point. Both are
/// parsed with the invariant culture and an explicit format (006, test 2), so a server
/// running in any culture reads 02/01 as 2 January. A <c>404</c> is read as "no values
/// in the range" (a weekend, a month IPCA has not published yet), not as an error.
/// </remarks>
public sealed class BcbSgsProvider(HttpClient http, IOptions<MarketDataOptions> options) : IBenchmarkProvider
{
    public const string Name = "BcbSgs";

    private const string DateFormat = "dd/MM/yyyy";

    public async Task<IReadOnlyList<DailyValue>> GetSeriesAsync(string code, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var bcb = options.Value.Bcb;
        if (!bcb.Series.TryGetValue(code, out var series))
        {
            throw new InvalidOperationException($"No SGS series is configured for benchmark {code} (MarketData:Bcb:Series).");
        }

        var uri = new Uri(
            $"{bcb.BaseUrl}{series.Code.ToString(CultureInfo.InvariantCulture)}/dados?formato=json"
            + $"&dataInicial={from.ToString(DateFormat, CultureInfo.InvariantCulture)}"
            + $"&dataFinal={to.ToString(DateFormat, CultureInfo.InvariantCulture)}");

        using var response = await http.GetAsync(uri, ct);
        ProviderResponse.ThrowIfRateLimited(response, Name);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();

        return await ProviderResponse.ReadAsync(
            response,
            Name,
            root => ProviderResponse.Within(
                root.EnumerateArray().Select(point => new DailyValue(
                    ProviderResponse.Date(point.GetProperty("data"), DateFormat),
                    ProviderResponse.Decimal(point.GetProperty("valor")))),
                value => value.Date,
                from,
                to),
            ct);
    }
}
