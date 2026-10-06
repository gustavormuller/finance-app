using Finance.Api.Application.Compare;
using Finance.Api.Domain.Transactions;

namespace Finance.Api.Endpoints;

/// <summary>
/// 026's route, <c>GET /api/compare</c>. The query is bound as strings and parsed here, so a
/// malformed one is a 400 naming the field in pt-BR, as 008's returns; whether a series exists
/// and can be converted is <see cref="CompareQueries"/>' to say. Shared market data, read
/// with a session like every other market-data route.
/// </summary>
public static class CompareEndpoints
{
    private const int MinimumSeries = 2;
    private const int MaximumSeries = 6;

    /// <summary>A code is a configuration key: letters and digits, as <c>CDI</c> or <c>IVVB11</c>.</summary>
    private const int CodeLength = 20;

    private const string AssetPrefix = "asset:";
    private const string BenchmarkPrefix = "benchmark:";

    private const string CountMessage = "Escolha de 2 a 6 séries para comparar.";

    private const string PeriodMessage =
        "O período deve ser 1 mês (1m), 6 meses (6m), no ano (ytd), 1 ano (1y), 5 anos (5y), 10 anos (10y), "
        + "máximo (max) ou personalizado (custom).";

    private const string OrderMessage = "A data inicial deve ser anterior à data final.";

    private const string CurrencyMessage = "A moeda deve ser a original (original), reais (BRL) ou dólares (USD).";

    public static IEndpointRouteBuilder MapCompareEndpoints(this IEndpointRouteBuilder routes)
    {
        routes.MapGet("/api/compare", async (
            CompareQueries queries,
            CancellationToken cancellationToken,
            string? series = null,
            string? period = null,
            string? from = null,
            string? to = null,
            string? currency = null) =>
        {
            var (request, problem) = Parse(series, period, from, to, currency);
            if (request is null)
            {
                return problem!;
            }

            var result = await queries.CompareAsync(request, cancellationToken);
            return result.View is { } view ? Results.Ok(view) : Problems.Validation(result.Violations)!;
        }).RequireAuthorization();

        return routes;
    }

    /// <summary>
    /// The request, or the 400 listing every field that is wrong. Dates alone mean a custom
    /// period, and nothing at all means 5A (decision 5).
    /// </summary>
    private static (CompareRequest? Request, IResult? Problem) Parse(
        string? series, string? period, string? from, string? to, string? currency)
    {
        var (keys, violations) = ParseSeries(series);
        var start = QueryDates.Parse(from, out var badFrom);
        var end = QueryDates.Parse(to, out var badTo);
        var kind = period?.ToLowerInvariant() switch
        {
            null => from is null && to is null ? ComparePeriodKind.FiveYears : ComparePeriodKind.Custom,
            "1m" => ComparePeriodKind.OneMonth,
            "6m" => ComparePeriodKind.SixMonths,
            "ytd" => ComparePeriodKind.Ytd,
            "1y" => ComparePeriodKind.OneYear,
            "5y" => ComparePeriodKind.FiveYears,
            "10y" => ComparePeriodKind.TenYears,
            "max" => ComparePeriodKind.Max,
            "custom" => ComparePeriodKind.Custom,
            _ => (ComparePeriodKind?)null,
        };
        var target = currency?.ToLowerInvariant() switch
        {
            null or "original" => CompareCurrency.Original,
            "brl" => CompareCurrency.Brl,
            "usd" => CompareCurrency.Usd,
            _ => (CompareCurrency?)null,
        };

        if (kind is null)
        {
            violations.Add(new RuleViolation("period", PeriodMessage));
        }
        else if (kind != ComparePeriodKind.Custom && (from is not null || to is not null))
        {
            violations.Add(new RuleViolation("period", QueryDates.OnlyForCustomPeriod));
        }

        if (badFrom)
        {
            violations.Add(new RuleViolation("from", QueryDates.FromFormat));
        }
        else if (start >= end)
        {
            violations.Add(new RuleViolation("from", OrderMessage));
        }

        if (badTo)
        {
            violations.Add(new RuleViolation("to", QueryDates.ToFormat));
        }

        if (target is null)
        {
            violations.Add(new RuleViolation("currency", CurrencyMessage));
        }

        return violations.Count > 0
            ? (null, Problems.Validation(violations))
            : (new CompareRequest(keys, kind!.Value, start, end, target!.Value), null);
    }

    /// <summary>The keys in the order given, or why they cannot be compared: how many, which is malformed, which repeats.</summary>
    private static (List<SeriesKey> Keys, List<RuleViolation> Violations) ParseSeries(string? series)
    {
        var items = (series ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (items.Length is < MinimumSeries or > MaximumSeries)
        {
            return ([], [new RuleViolation("series", CountMessage)]);
        }

        var keys = new List<SeriesKey>();
        var violations = new List<RuleViolation>();
        foreach (var item in items)
        {
            if (Key(item) is not { } key)
            {
                violations.Add(new RuleViolation("series", $"Série não reconhecida: '{item}'. Use asset:{{id}} ou benchmark:{{código}}."));
            }
            else if (keys.Contains(key))
            {
                violations.Add(new RuleViolation("series", $"A série '{key}' foi escolhida mais de uma vez."));
            }
            else
            {
                keys.Add(key);
            }
        }

        return (keys, violations);
    }

    private static SeriesKey? Key(string item)
    {
        if (item.StartsWith(AssetPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return Guid.TryParse(item[AssetPrefix.Length..], out var id) ? SeriesKey.Asset(id) : null;
        }

        if (item.StartsWith(BenchmarkPrefix, StringComparison.OrdinalIgnoreCase))
        {
            var code = item[BenchmarkPrefix.Length..];
            return code.Length is > 0 and <= CodeLength && code.All(char.IsAsciiLetterOrDigit) ? SeriesKey.Benchmark(code) : null;
        }

        return null;
    }
}
