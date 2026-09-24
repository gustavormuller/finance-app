using System.Text.Json;
using Finance.Api.Domain.Investments;
using Finance.Api.Domain.MarketData;
using Microsoft.Extensions.Time.Testing;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 008's integration tests write <see cref="PortfolioDaily"/> rows and benchmark values
/// straight to the database, so every value the endpoint reads is one the test chose and
/// the expected returns can be computed by hand. The movements still go through
/// <see cref="InvestmentsApi.HoldAsync"/>.
/// </summary>
internal static class ReturnsFixtures
{
    /// <summary>A fixed "now" at midday UTC, so the UTC date is <paramref name="today"/>.</summary>
    public static FakeTimeProvider ClockOn(DateOnly today) =>
        new(new DateTimeOffset(today.ToDateTime(new TimeOnly(12, 0)), TimeSpan.Zero));

    /// <summary>One row per date given; <c>ValueBrl</c> is the value as given.</summary>
    public static async Task DailyAsync(
        this InvestmentsApi api, Asset asset, CancellationToken cancellationToken,
        params (DateOnly Date, decimal Quantity, decimal Price, decimal Fx, decimal ValueBrl)[] rows)
    {
        await using var context = api.Context(asset.UserId);
        context.AddRange(rows.Select(row => new PortfolioDaily
        {
            UserId = asset.UserId,
            AssetId = asset.Id,
            Date = row.Date,
            Quantity = row.Quantity,
            Price = row.Price,
            PriceDate = row.Date,
            FxRate = row.Fx,
            ValueBrl = row.ValueBrl,
        }));
        await context.SaveChangesAsync(cancellationToken);
    }

    /// <summary>A BRL row for every day from <paramref name="from"/> to <paramref name="to"/>, at <paramref name="price"/>.</summary>
    public static Task DailyAsync(
        this InvestmentsApi api, Asset asset, DateOnly from, DateOnly to, decimal quantity, decimal price,
        CancellationToken cancellationToken) =>
        api.DailyAsync(asset, cancellationToken, [.. Enumerable.Range(0, to.DayNumber - from.DayNumber + 1)
            .Select(offset => (from.AddDays(offset), quantity, price, 1m, quantity * price))]);

    public static async Task BenchmarkAsync(
        this InvestmentsApi api, string code, CancellationToken cancellationToken, params (DateOnly Date, decimal Value)[] values)
    {
        await using var context = api.Context(null);
        context.AddRange(values.Select(value => new Benchmark { Code = code, Date = value.Date, Value = value.Value }));
        await context.SaveChangesAsync(cancellationToken);
    }
}

/// <summary>The returns response as the web client reads it; <c>Fx</c> is only on the asset route.</summary>
internal sealed record ReturnsBody(
    PeriodBody? Period,
    TwrBody? Twr,
    decimal? Xirr,
    decimal? TimingEffect,
    FxBody? Fx,
    Dictionary<string, BenchmarkBody?> Benchmarks,
    List<Dictionary<string, JsonElement>> Series);

internal sealed record PeriodBody(DateOnly From, DateOnly To, int Days);

internal sealed record TwrBody(decimal Total, decimal? Annualised);

internal sealed record BenchmarkBody(decimal Total, decimal? Annualised);

internal sealed record FxBody(decimal Native, decimal Fx, decimal Total);
