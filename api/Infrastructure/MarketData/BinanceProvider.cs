using System.Globalization;
using System.Net;
using System.Text.Json;
using Finance.Api.Application.MarketData;
using Finance.Api.Domain.MarketData;
using Microsoft.Extensions.Options;

namespace Finance.Api.Infrastructure.MarketData;

/// <summary>
/// Binance's public spot market, for crypto pairs quoted in BRL (019):
/// <c>klines?symbol=BTCBRL&amp;interval=1d&amp;startTime=...&amp;endTime=...&amp;limit=1000</c>,
/// answering <c>[[openTimeMs, "open", "high", "low", "close", ...], ...]</c>. No key.
/// </summary>
/// <remarks>
/// A candle's day is the UTC day it opens; a daily candle closes at 23:59:59.999 UTC, so
/// its close is that day's last price. Closes are strings, parsed to <c>decimal</c> with
/// the invariant culture. A request returns at most <see cref="PageSize"/> candles, so a
/// long range is read in pages, each starting 1 ms after the previous page's last open.
/// An unknown symbol is a <c>400</c> with code <c>-1121</c> and reads as an empty series.
/// Binance answers <c>418</c> to an IP that kept calling after a <c>429</c>; both are a
/// rate limit.
/// </remarks>
public sealed class BinanceProvider(HttpClient http, IOptions<MarketDataOptions> options) : IPriceProvider
{
    public const string Name = "Binance";

    /// <summary>Binance's cap on candles per klines request.</summary>
    public const int PageSize = 1000;

    private const int UnknownSymbol = -1121;

    private const HttpStatusCode Banned = (HttpStatusCode)418;

    public ProviderKind Kind => ProviderKind.Binance;

    public async Task<IReadOnlyList<DailyClose>> GetDailyClosesAsync(
        string providerSymbol, DateOnly from, DateOnly to, CancellationToken ct)
    {
        // Binance refuses a lowercase symbol outright.
        var symbol = providerSymbol.Trim().ToUpperInvariant();
        var start = StartOf(from);
        var end = StartOf(to.AddDays(1)) - 1;

        var closes = new List<DailyClose>();
        while (true)
        {
            var page = await PageAsync(symbol, start, end, ct);
            closes.AddRange(page.Select(candle => candle.Close));
            if (page.Count < PageSize || page[^1].OpenTime < start)
            {
                break;
            }

            start = page[^1].OpenTime + 1;
        }

        return ProviderResponse.Within(closes, close => close.Date, from, to);
    }

    private async Task<IReadOnlyList<Candle>> PageAsync(string symbol, long start, long end, CancellationToken ct)
    {
        var uri = new Uri(
            new Uri(options.Value.Binance.BaseUrl),
            $"klines?symbol={Uri.EscapeDataString(symbol)}&interval=1d"
            + $"&startTime={start.ToString(CultureInfo.InvariantCulture)}"
            + $"&endTime={end.ToString(CultureInfo.InvariantCulture)}"
            + $"&limit={PageSize.ToString(CultureInfo.InvariantCulture)}");

        using var response = await http.GetAsync(uri, ct);
        ProviderResponse.ThrowIfRateLimited(response, Name);
        if (response.StatusCode == Banned)
        {
            throw new ProviderRateLimitedException(Name, response.Headers.RetryAfter?.Delta);
        }

        if (response.StatusCode == HttpStatusCode.BadRequest)
        {
            var (code, message) = await ProviderResponse.ReadAsync(
                response, Name, root => (root.GetProperty("code").GetInt32(), root.GetProperty("msg").GetString() ?? ""), ct);
            return code == UnknownSymbol
                ? []
                : throw new HttpRequestException($"{Name} answered error {code}: {message}", null, HttpStatusCode.BadRequest);
        }

        response.EnsureSuccessStatusCode();

        return await ProviderResponse.ReadAsync(response, Name, Candles, ct);
    }

    private static IReadOnlyList<Candle> Candles(JsonElement root) =>
        [.. root.EnumerateArray().Select(candle =>
        {
            var openTime = candle[0].GetInt64();
            var day = DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeMilliseconds(openTime).UtcDateTime);
            return new Candle(openTime, new DailyClose(day, ProviderResponse.Decimal(candle[4])));
        })];

    private static long StartOf(DateOnly day) =>
        new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeMilliseconds();

    private readonly record struct Candle(long OpenTime, DailyClose Close);
}
