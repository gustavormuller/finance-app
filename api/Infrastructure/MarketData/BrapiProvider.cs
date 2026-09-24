using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Finance.Api.Application.MarketData;
using Finance.Api.Domain.MarketData;
using Microsoft.Extensions.Options;

namespace Finance.Api.Infrastructure.MarketData;

/// <summary>
/// brapi.dev, for B3 stocks, FIIs, ETFs and BDRs:
/// <c>quote/{symbol}?range=...&amp;interval=1d</c>, closes under
/// <c>results[0].historicalDataPrice[].{date, close}</c> with <c>date</c> in Unix seconds.
/// </summary>
/// <remarks>
/// brapi takes a named range, not dates, so the adapter asks for the smallest range that
/// reaches <c>from</c> and keeps the days inside <c>[from, to]</c>. A timestamp's day is
/// its day in Sao Paulo, fixed at UTC-3 (Brazil has kept no daylight saving time since
/// 2019, and a fixed offset needs no time-zone database in the container). An unknown
/// ticker is a <c>404</c> and reads as an empty series (006, test 4); so does a day
/// with a null close.
/// </remarks>
public sealed class BrapiProvider(HttpClient http, IOptions<MarketDataOptions> options, TimeProvider clock) : IPriceProvider
{
    public const string Name = "Brapi";

    private static readonly TimeSpan SaoPaulo = TimeSpan.FromHours(-3);

    public ProviderKind Kind => ProviderKind.Brapi;

    public async Task<IReadOnlyList<DailyClose>> GetDailyClosesAsync(
        string providerSymbol, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var brapi = options.Value.Brapi;
        var today = DateOnly.FromDateTime(clock.GetUtcNow().ToOffset(SaoPaulo).DateTime);

        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(new Uri(brapi.BaseUrl), $"quote/{Uri.EscapeDataString(providerSymbol)}?range={RangeReaching(from, today)}&interval=1d"));
        if (!string.IsNullOrEmpty(brapi.Token))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", brapi.Token);
        }

        using var response = await http.SendAsync(request, ct);
        ProviderResponse.ThrowIfRateLimited(response, Name);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        response.EnsureSuccessStatusCode();

        return await ProviderResponse.ReadAsync(
            response, Name, root => ProviderResponse.Within(Closes(root), close => close.Date, from, to), ct);
    }

    private static IEnumerable<DailyClose> Closes(JsonElement root)
    {
        foreach (var result in root.GetProperty("results").EnumerateArray())
        {
            if (!result.TryGetProperty("historicalDataPrice", out var history))
            {
                continue;
            }

            foreach (var point in history.EnumerateArray())
            {
                var close = point.GetProperty("close");
                if (close.ValueKind == JsonValueKind.Null)
                {
                    continue;
                }

                var day = DateTimeOffset.FromUnixTimeSeconds(point.GetProperty("date").GetInt64()).ToOffset(SaoPaulo);
                yield return new DailyClose(DateOnly.FromDateTime(day.DateTime), ProviderResponse.Decimal(close));
            }
        }
    }

    /// <summary>brapi's named ranges, smallest first; <c>max</c> when none reaches <paramref name="from"/>.</summary>
    internal static string RangeReaching(DateOnly from, DateOnly today) =>
        from >= today.AddDays(-4) ? "5d"
        : from >= today.AddMonths(-1) ? "1mo"
        : from >= today.AddMonths(-3) ? "3mo"
        : from >= today.AddMonths(-6) ? "6mo"
        : from >= today.AddYears(-1) ? "1y"
        : from >= today.AddYears(-2) ? "2y"
        : from >= today.AddYears(-5) ? "5y"
        : from >= today.AddYears(-10) ? "10y"
        : "max";
}
