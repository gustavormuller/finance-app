using System.Globalization;
using System.Net;
using System.Text.Json;
using Finance.Api.Application.MarketData;
using Finance.Api.Domain.MarketData;
using Microsoft.Extensions.Options;

namespace Finance.Api.Infrastructure.MarketData;

/// <summary>
/// CoinGecko, for crypto: <c>coins/{id}/market_chart?vs_currency=usd&amp;days=N&amp;interval=daily</c>,
/// answering <c>{"prices":[[unixMilliseconds, price], ...]}</c>.
/// </summary>
/// <remarks>
/// Asks for the days from <c>from</c> to today, capped at <c>MaxHistoryDays</c> (the
/// public and demo plans serve 365). A timestamp's day is its UTC day (006, test 5).
/// Daily points sit at 00:00 UTC and the last point is "now", so a day can appear twice;
/// it keeps its first point, the daily one. Prices must be JSON numbers and are read
/// straight to <c>decimal</c>. An unknown coin id is a <c>404</c> and an empty series.
/// </remarks>
public sealed class CoinGeckoProvider(HttpClient http, IOptions<MarketDataOptions> options, TimeProvider clock) : IPriceProvider
{
    public const string Name = "CoinGecko";

    public ProviderKind Kind => ProviderKind.CoinGecko;

    public async Task<IReadOnlyList<DailyClose>> GetDailyClosesAsync(
        string providerSymbol, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var coinGecko = options.Value.CoinGecko;
        var today = DateOnly.FromDateTime(clock.GetUtcNow().UtcDateTime);
        var days = Math.Max(1, today.DayNumber - from.DayNumber + 1);
        if (coinGecko.MaxHistoryDays > 0)
        {
            days = Math.Min(days, coinGecko.MaxHistoryDays);
        }

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(
                new Uri(coinGecko.BaseUrl),
                $"coins/{Uri.EscapeDataString(providerSymbol)}/market_chart"
                + $"?vs_currency={Uri.EscapeDataString(coinGecko.VsCurrency)}"
                + $"&days={days.ToString(CultureInfo.InvariantCulture)}&interval=daily"));
        if (!string.IsNullOrEmpty(coinGecko.DemoKey))
        {
            request.Headers.Add("x-cg-demo-api-key", coinGecko.DemoKey);
        }

        using var response = await http.SendAsync(request, ct);
        ProviderResponse.ThrowIfRateLimited(response, Name);
        ProviderResponse.ThrowIfKeyMissing(response, Name, coinGecko.DemoKey, providerSymbol, CoinGeckoOptions.DemoKeySetting);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();

        return await ProviderResponse.ReadAsync(
            response, Name, root => ProviderResponse.Within(FirstPerDay(root), close => close.Date, from, to), ct);
    }

    private static IEnumerable<DailyClose> FirstPerDay(JsonElement root)
    {
        var seen = new HashSet<DateOnly>();
        foreach (var point in root.GetProperty("prices").EnumerateArray())
        {
            var instant = DateTimeOffset.FromUnixTimeMilliseconds(point[0].GetInt64());
            var close = new DailyClose(DateOnly.FromDateTime(instant.UtcDateTime), point[1].GetDecimal());
            if (seen.Add(close.Date))
            {
                yield return close;
            }
        }
    }
}
