using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Finance.Api.Application.MarketData;
using Finance.Api.Domain.MarketData;
using Microsoft.Extensions.Options;

namespace Finance.Api.Infrastructure.MarketData;

/// <summary>
/// Yahoo Finance's chart endpoint (025), the primary price source: B3 (<c>PETR4.SA</c>), US
/// stocks, indices (<c>^BVSP</c>), crypto in dollars (<c>BTC-USD</c>) and FX (<c>BRL=X</c>):
/// <c>{symbol}?period1=...&amp;period2=...&amp;interval=1d&amp;events=div%2Csplit&amp;includeAdjustedClose=true</c>.
/// No key, no cookie; unofficial and for personal use only (ARCHITECTURE.md).
/// </summary>
/// <remarks>
/// <para>
/// <c>period2</c> is always now. Yahoo's close is split-adjusted backwards in time and the
/// response lists only the splits inside the window, so the window must run to now for every
/// split Yahoo already applied to be there. The raw close is that close times the ratio of
/// every split dated after its day, rounded to <c>meta.priceHint</c> decimals (decision 6).
/// <c>adjclose</c> is stored as sent (decision 7).
/// </para>
/// <para>
/// A bar is stamped at its session's start, local time: mid-morning for an exchange,
/// midnight for FX (London) and crypto (UTC). Its day is that stamp at <c>meta.gmtoffset</c>,
/// read one hour later. The offset is today's, and a bar from the other half of the year was
/// stamped an hour off it: a London-midnight bar would fall on the day before. No session
/// starts after 22:00, so an hour later never reaches the next day. The IANA zone is not
/// used: with invariant globalization Windows finds none (decision 5).
/// </para>
/// <para>
/// Only completed sessions are returned: a day on or before <c>to</c> and before the
/// exchange's today. Only a day's first point is its bar; FX appends a quote stamped "now",
/// which never stands in for a null close. Null closes are skipped.
/// </para>
/// </remarks>
public sealed class YahooProvider(HttpClient http, IOptions<MarketDataOptions> options, TimeProvider clock, YahooPacer pacer)
    : IPriceProvider
{
    public const string Name = "Yahoo";

    /// <summary>Where a whole history starts: <c>period1=-2208988800</c>, the earliest Yahoo takes.</summary>
    public static readonly DateOnly WholeHistory = new(1900, 1, 1);

    /// <summary>A stamp is read this much later than its offset says (see remarks).</summary>
    private static readonly TimeSpan DaylightSlack = TimeSpan.FromHours(1);

    /// <summary>What Yahoo answers, with HTTP 400, for a window before the first trade.</summary>
    private const string NoDataInWindow = "Data doesn't exist";

    public ProviderKind Kind => ProviderKind.Yahoo;

    public DateOnly? HistoryStart => WholeHistory;

    public bool RevisesHistory => true;

    public async Task<IReadOnlyList<DailyClose>> GetDailyClosesAsync(
        string providerSymbol, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var yahoo = options.Value.Yahoo;
        var symbol = providerSymbol.Trim().ToUpperInvariant();
        var now = clock.GetUtcNow();

        // The day before `from`: a London-midnight bar for `from` is stamped 23:00 UTC the day before.
        var period1 = Math.Max(UnixSeconds(WholeHistory), UnixSeconds(from.AddDays(-1)));
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(
                new Uri(yahoo.BaseUrl),
                $"{Uri.EscapeDataString(symbol)}?period1={period1.ToString(CultureInfo.InvariantCulture)}"
                + $"&period2={now.ToUnixTimeSeconds().ToString(CultureInfo.InvariantCulture)}"
                + "&interval=1d&events=div%2Csplit&includeAdjustedClose=true"));
        request.Headers.TryAddWithoutValidation("User-Agent", yahoo.UserAgent);
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        await pacer.WaitTurnAsync(yahoo.RequestInterval, ct);
        using var response = await http.SendAsync(request, ct);
        ProviderResponse.ThrowIfRateLimited(response, Name);
        if (ProviderResponse.IsRefusal(response.StatusCode))
        {
            throw new ProviderBlockedException(Name, symbol);
        }

        if (response.StatusCode is HttpStatusCode.NotFound or HttpStatusCode.BadRequest)
        {
            var (code, description) = await ProviderResponse.ReadAsync(response, Name, Error, ct);
            return response.StatusCode == HttpStatusCode.NotFound
                ? throw new ProviderSymbolUnknownException(Name, symbol)
                : description.StartsWith(NoDataInWindow, StringComparison.Ordinal)
                    ? []
                    : throw new HttpRequestException($"{Name} answered {code}: {description}", null, response.StatusCode);
        }

        response.EnsureSuccessStatusCode();

        return await ProviderResponse.ReadAsync(
            response, Name, root => ProviderResponse.Within(Closes(root, now), close => close.Date, from, to), ct);
    }

    private static (string Code, string Description) Error(JsonElement root)
    {
        var error = root.GetProperty("chart").GetProperty("error");
        return (error.GetProperty("code").GetString() ?? "", error.GetProperty("description").GetString() ?? "");
    }

    /// <summary>Every completed session's close, raw, with Yahoo's adjusted close beside it.</summary>
    private static List<DailyClose> Closes(JsonElement root, DateTimeOffset now)
    {
        var result = root.GetProperty("chart").GetProperty("result");
        if (result.ValueKind != JsonValueKind.Array || result.GetArrayLength() == 0)
        {
            throw new FormatException("The chart has no result.");
        }

        var series = result[0];
        var meta = series.GetProperty("meta");
        var offset = meta.TryGetProperty("gmtoffset", out var seconds) ? TimeSpan.FromSeconds(seconds.GetInt32()) : TimeSpan.Zero;

        // A window with no session at all: meta, and no timestamp.
        if (!series.TryGetProperty("timestamp", out var timestamps))
        {
            return [];
        }

        var today = DateOnly.FromDateTime(now.ToOffset(offset).DateTime);
        int? priceHint = meta.TryGetProperty("priceHint", out var hint) ? hint.GetInt32() : null;
        var splits = Splits(series, offset);
        var indicators = series.GetProperty("indicators");
        var close = indicators.GetProperty("quote")[0].GetProperty("close");
        JsonElement? adjusted = indicators.TryGetProperty("adjclose", out var adjclose)
            && adjclose[0].TryGetProperty("adjclose", out var values) ? values : null;

        var closes = new List<DailyClose>();
        var days = new HashSet<DateOnly>();
        for (var i = 0; i < timestamps.GetArrayLength(); i++)
        {
            var day = DayOf(timestamps[i].GetInt64(), offset);
            if (!days.Add(day) || close[i].ValueKind == JsonValueKind.Null || day >= today)
            {
                continue;
            }

            var raw = ProviderResponse.Decimal(close[i]) * splits.Where(split => split.Day > day).Aggregate(1m, (product, split) => product * split.Ratio);
            closes.Add(new DailyClose(
                day,
                priceHint is >= 0 and <= 8 ? Math.Round(raw, priceHint.Value, MidpointRounding.ToEven) : raw,
                adjusted is { } sent && sent[i].ValueKind != JsonValueKind.Null ? ProviderResponse.Decimal(sent[i]) : null));
        }

        return closes;
    }

    /// <summary>Each split's day and how many shares one became (<c>numerator / denominator</c>).</summary>
    private static List<(DateOnly Day, decimal Ratio)> Splits(JsonElement series, TimeSpan offset)
    {
        if (!series.TryGetProperty("events", out var events) || !events.TryGetProperty("splits", out var splits))
        {
            return [];
        }

        return [.. splits.EnumerateObject().Select(entry =>
        {
            var split = entry.Value;
            var numerator = ProviderResponse.Decimal(split.GetProperty("numerator"));
            var denominator = ProviderResponse.Decimal(split.GetProperty("denominator"));
            return numerator <= 0 || denominator <= 0
                ? throw new FormatException($"Split {entry.Name} has the ratio {numerator}:{denominator}.")
                : (DayOf(split.GetProperty("date").GetInt64(), offset), numerator / denominator);
        })];
    }

    private static DateOnly DayOf(long unixSeconds, TimeSpan offset) =>
        DateOnly.FromDateTime(DateTimeOffset.FromUnixTimeSeconds(unixSeconds).ToOffset(offset).DateTime + DaylightSlack);

    private static long UnixSeconds(DateOnly day) =>
        new DateTimeOffset(day.ToDateTime(TimeOnly.MinValue), TimeSpan.Zero).ToUnixTimeSeconds();
}
