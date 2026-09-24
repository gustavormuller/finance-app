using System.Globalization;
using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Finance.Api.Application.MarketData;
using Finance.Api.Domain.MarketData;
using Microsoft.Extensions.Options;

namespace Finance.Api.Infrastructure.MarketData;

/// <summary>
/// Twelve Data, for US stocks:
/// <c>time_series?symbol=AAPL&amp;interval=1day&amp;start_date=...&amp;end_date=...&amp;outputsize=5000</c>,
/// answering <c>{"values":[{"datetime":"2024-01-02","close":"185.64000"}],"status":"ok"}</c>.
/// </summary>
/// <remarks>
/// Closes are strings and parse to <c>decimal</c> with the invariant culture. Newest
/// first on the wire, oldest first here. <c>end_date</c> is asked one day past
/// <c>to</c> and the result trimmed to <c>[from, to]</c>, so the last day is in whether
/// the API treats the bound as inclusive or not. Errors may arrive with HTTP 200 and
/// <c>{"status":"error","code":...}</c> in the body: 429 is a rate limit, 404 or "no
/// data" is an empty series, anything else an <see cref="HttpRequestException"/>
/// with that code.
/// </remarks>
public sealed class TwelveDataProvider(HttpClient http, IOptions<MarketDataOptions> options) : IPriceProvider
{
    public const string Name = "TwelveData";

    private const string DateFormat = "yyyy-MM-dd";

    public ProviderKind Kind => ProviderKind.TwelveData;

    public async Task<IReadOnlyList<DailyClose>> GetDailyClosesAsync(
        string providerSymbol, DateOnly from, DateOnly to, CancellationToken ct)
    {
        var twelveData = options.Value.TwelveData;
        using var request = new HttpRequestMessage(
            HttpMethod.Get,
            new Uri(
                new Uri(twelveData.BaseUrl),
                $"time_series?symbol={Uri.EscapeDataString(providerSymbol)}&interval=1day"
                + $"&start_date={from.ToString(DateFormat, CultureInfo.InvariantCulture)}"
                + $"&end_date={to.AddDays(1).ToString(DateFormat, CultureInfo.InvariantCulture)}&outputsize=5000"));
        if (!string.IsNullOrEmpty(twelveData.Key))
        {
            request.Headers.Authorization = new AuthenticationHeaderValue("apikey", twelveData.Key);
        }

        using var response = await http.SendAsync(request, ct);
        ProviderResponse.ThrowIfRateLimited(response, Name);
        if (response.StatusCode == HttpStatusCode.NotFound)
        {
            return [];
        }

        // A 400 carries the same error body as a 200 does, "no data" included.
        if (response.StatusCode != HttpStatusCode.BadRequest)
        {
            response.EnsureSuccessStatusCode();
        }

        return await ProviderResponse.ReadAsync(response, Name, root => Closes(root, from, to), ct);
    }

    private static IReadOnlyList<DailyClose> Closes(JsonElement root, DateOnly from, DateOnly to)
    {
        if (root.TryGetProperty("status", out var status) && status.ValueKind == JsonValueKind.String
            && status.GetString() == "error")
        {
            var code = root.GetProperty("code").GetInt32();
            var message = root.TryGetProperty("message", out var text) ? text.GetString() ?? "" : "";
            return code switch
            {
                429 => throw new ProviderRateLimitedException(Name, retryAfter: null),
                404 => [],
                400 when message.StartsWith("No data is available", StringComparison.OrdinalIgnoreCase) => [],
                _ => throw new HttpRequestException($"{Name} answered error {code}: {message}", null, (HttpStatusCode)code),
            };
        }

        return ProviderResponse.Within(
            root.GetProperty("values").EnumerateArray().Select(point => new DailyClose(
                ProviderResponse.Date(point.GetProperty("datetime"), DateFormat),
                ProviderResponse.Decimal(point.GetProperty("close")))),
            close => close.Date,
            from,
            to);
    }
}
