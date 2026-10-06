using Finance.Api.Domain.MarketData;
using Finance.Api.Domain.Transactions;

namespace Finance.Api.Application.Compare;

/// <summary>A series as asked for: a catalogue asset, or a stored benchmark series by its code.</summary>
public sealed record SeriesKey(Guid? AssetId, string? Code)
{
    public static SeriesKey Asset(Guid id) => new(id, null);

    /// <summary>Codes are configured, and so stored, in capitals.</summary>
    public static SeriesKey Benchmark(string code) => new(null, code.ToUpperInvariant());

    /// <summary>As written on the wire: <c>asset:{id}</c> or <c>benchmark:{code}</c>.</summary>
    public override string ToString() => AssetId is { } id ? $"asset:{id}" : $"benchmark:{Code}";
}

/// <summary>Each series in its own currency, or every one in reais or in dollars (026, decision 10).</summary>
public enum CompareCurrency
{
    Original = 0,
    Brl = 1,
    Usd = 2,
}

/// <summary>What <c>GET /api/compare</c> was asked for, parsed.</summary>
public sealed record CompareRequest(
    IReadOnlyList<SeriesKey> Series, ComparePeriodKind Period, DateOnly? From, DateOnly? To, CompareCurrency Currency);

/// <summary><c>From</c> is the start, where every series is 100.</summary>
public sealed record ComparePeriodView(DateOnly From, DateOnly To, int Days);

/// <summary>
/// One series. <c>Currency</c> is its own; <c>FirstDate</c> is where its data begins in the
/// currency it is drawn in, <c>LastDate</c> its last observation. <c>Change</c> and
/// <c>Annualised</c> are <c>null</c> when it is not drawn.
/// </summary>
public sealed record CompareSeriesView(
    string Key,
    string Kind,
    string? Ticker,
    string? Name,
    MarketAssetClass? Class,
    string? Code,
    string Currency,
    DateOnly? FirstDate,
    DateOnly? LastDate,
    bool HasData,
    decimal? Change,
    decimal? Annualised);

/// <summary>One date of the chart: each series' index, in the series' order, <c>null</c> for one not drawn.</summary>
public sealed record ComparePointView(DateOnly Date, IReadOnlyList<decimal?> Values);

/// <summary>026's response. <c>Period</c> is <c>null</c> and <c>Points</c> empty when nothing is drawn (decision 9).</summary>
public sealed record CompareView(
    string Currency, ComparePeriodView? Period, IReadOnlyList<CompareSeriesView> Series, IReadOnlyList<ComparePointView> Points);

/// <summary>The comparison, or every reason the request names something that cannot be compared.</summary>
public sealed record CompareResult(CompareView? View, IReadOnlyList<RuleViolation> Violations);
