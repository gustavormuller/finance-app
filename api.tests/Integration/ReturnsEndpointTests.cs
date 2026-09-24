using System.Net;
using System.Net.Http.Json;
using static Finance.Api.Tests.Integration.InvestmentsApi;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 008 checkpoint 4: <c>GET /api/returns/portfolio</c>. Spec integration tests 27–30, and
/// the query's validation. Values are written to the database by hand
/// (<see cref="ReturnsFixtures"/>), so every expected number is computed from them.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ReturnsEndpointTests(PostgresFixture postgres)
{
    private static readonly DateOnly MidYear = new(2026, 6, 30);
    private static readonly DateOnly Bought = new(2025, 11, 3);

    /// <summary>Spec integration test 27.</summary>
    [Fact]
    public async Task Another_users_returns_are_empty_and_their_asset_is_not_found()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct, ReturnsFixtures.ClockOn(MidYear));
        var a = await api.SignInAsync("holder", ct);
        var b = await api.SignInAsync("other", ct);
        var asset = await HeldSinceNovemberAsync(api, a.Id, ct);

        var theirs = await b.Client.GetFromJsonAsync<ReturnsBody>("/api/returns/portfolio", ct);
        using var theirAsset = await b.Client.GetAsync($"/api/returns/assets/{asset.Id}", ct);
        var mine = await a.Client.GetFromJsonAsync<ReturnsBody>("/api/returns/portfolio", ct);

        Assert.Equal((null, null, null, null), (theirs!.Period, theirs.Twr, theirs.Xirr, theirs.TimingEffect));
        Assert.Empty(theirs.Series);
        Assert.All(theirs.Benchmarks.Values, Assert.Null);
        Assert.Equal(HttpStatusCode.NotFound, theirAsset.StatusCode);
        Assert.Equal(0.155m, mine!.Twr!.Total);
    }

    /// <summary>Spec integration test 28, with inception and 12 months beside it.</summary>
    [Fact]
    public async Task Ytd_runs_from_the_first_of_january_on_a_mid_year_call()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct, ReturnsFixtures.ClockOn(MidYear));
        var user = await api.SignInAsync("investor", ct);
        await HeldSinceNovemberAsync(api, user.Id, ct);

        var ytd = await user.Client.GetFromJsonAsync<ReturnsBody>("/api/returns/portfolio?period=ytd", ct);
        var inception = await user.Client.GetFromJsonAsync<ReturnsBody>("/api/returns/portfolio", ct);
        var twelve = await user.Client.GetFromJsonAsync<ReturnsBody>("/api/returns/portfolio?period=12m", ct);

        // The base day is 2025-12-31 at 1050, so 1 January's step to 1155 counts: +10%.
        Assert.Equal(new PeriodBody(new DateOnly(2026, 1, 1), MidYear, 181), ytd!.Period);
        Assert.Equal(0.1m, ytd.Twr!.Total);
        Assert.Equal(new PeriodBody(Bought, MidYear, 239), inception!.Period);
        Assert.Equal(0.155m, inception.Twr!.Total);

        // A year back is before the first movement, so 12m clamps to inception.
        Assert.Equal(inception.Period, twelve!.Period);
    }

    /// <summary>Spec integration test 29.</summary>
    [Fact]
    public async Task A_from_before_the_first_movement_is_clamped_to_it()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct, ReturnsFixtures.ClockOn(MidYear));
        var user = await api.SignInAsync("investor", ct);
        await HeldSinceNovemberAsync(api, user.Id, ct);

        var body = await user.Client.GetFromJsonAsync<ReturnsBody>(
            "/api/returns/portfolio?period=custom&from=2020-01-01&to=2026-01-31", ct);

        Assert.Equal(new PeriodBody(Bought, new DateOnly(2026, 1, 31), 89), body!.Period);
        Assert.Equal(0.155m, body.Twr!.Total);
        Assert.Equal((Bought, 100m), (body.Series[0]["date"].GetDateOnly(), body.Series[0]["portfolio"].GetDecimal()));
    }

    /// <summary>Spec integration test 30.</summary>
    [Fact]
    public async Task A_five_year_series_is_sampled_to_at_most_260_points()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct, ReturnsFixtures.ClockOn(MidYear));
        var user = await api.SignInAsync("investor", ct);
        var start = MidYear.AddYears(-5);
        var petr4 = await api.CatalogueAsync("PETR4", ct);
        var asset = await api.HoldAsync(user.Id, petr4, ct, Buy(start, 100m, 10m));
        await api.DailyAsync(asset, start, MidYear, 100m, 10m, ct);

        var body = await user.Client.GetFromJsonAsync<ReturnsBody>("/api/returns/portfolio", ct);

        Assert.Equal(1826, body!.Period!.Days);
        Assert.InRange(body.Series.Count, 200, 260);
        Assert.Equal(start, body.Series[0]["date"].GetDateOnly());
        Assert.Equal(MidYear, body.Series[^1]["date"].GetDateOnly());
        Assert.All(body.Series, point => Assert.Equal(100m, point["portfolio"].GetDecimal()));
    }

    [Theory]
    [InlineData("period=weekly", "period")]
    [InlineData("period=ytd&from=2026-01-01", "period")]
    [InlineData("from=2026-13-01", "from")]
    [InlineData("to=30/06/2026", "to")]
    [InlineData("from=2026-06-30&to=2026-06-01", "from")]
    public async Task A_malformed_query_is_a_400_in_portuguese_naming_the_field(string query, string field)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct, ReturnsFixtures.ClockOn(MidYear));
        var user = await api.SignInAsync("investor", ct);

        using var response = await user.Client.GetAsync("/api/returns/portfolio?" + query, ct);
        var problem = await response.Content.ReadFromJsonAsync<ProblemBody>(ct);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        var message = Assert.Single(problem!.Errors[field]);
        Assert.Matches("[ãáéíóúçà]|deve", message);
    }

    [Fact]
    public async Task The_portfolio_route_needs_a_session()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct, ReturnsFixtures.ClockOn(MidYear));
        var anonymous = api.Factory.CreateApiClient();

        using var portfolio = await anonymous.GetAsync("/api/returns/portfolio", ct);

        Assert.Equal(HttpStatusCode.Unauthorized, portfolio.StatusCode);
    }

    private sealed record ProblemBody(Dictionary<string, string[]> Errors);

    /// <summary>
    /// 100 PETR4 bought 2025-11-03 at 10. Worth 1000 to 15 December, 1050 to the end of the
    /// year, and 1155 from 1 January to 30 June 2026: +15.5% since inception.
    /// </summary>
    private static async Task<Finance.Api.Domain.Investments.Asset> HeldSinceNovemberAsync(
        InvestmentsApi api, Guid userId, CancellationToken ct)
    {
        var petr4 = await api.CatalogueAsync("PETR4", ct);
        var asset = await api.HoldAsync(userId, petr4, ct, Buy(Bought, 100m, 10m));
        await api.DailyAsync(asset, Bought, new DateOnly(2025, 12, 15), 100m, 10m, ct);
        await api.DailyAsync(asset, new DateOnly(2025, 12, 16), new DateOnly(2025, 12, 31), 100m, 10.5m, ct);
        await api.DailyAsync(asset, new DateOnly(2026, 1, 1), MidYear, 100m, 11.55m, ct);
        return asset;
    }
}

internal static class JsonDates
{
    public static DateOnly GetDateOnly(this System.Text.Json.JsonElement element) =>
        DateOnly.ParseExact(element.GetString()!, "yyyy-MM-dd", System.Globalization.CultureInfo.InvariantCulture);
}
