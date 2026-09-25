using System.Net;
using System.Net.Http.Json;
using static Finance.Api.Tests.Integration.InvestmentsApi;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// <c>GET /api/returns/assets/{id}</c>, one asset's returns with its FX split (008,
/// decision 8; unit tests 22–24 at the endpoint). XIRR checked in LibreOffice Calc:
/// <c>0.475054807440193</c>.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class ReturnsAssetEndpointTests(PostgresFixture postgres)
{
    private static readonly DateOnly Bought = new(2026, 1, 2);
    private static readonly DateOnly MidYear = new(2026, 6, 30);

    [Fact]
    public async Task A_usd_asset_splits_into_its_own_return_and_the_dollars()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct, ReturnsFixtures.ClockOn(MidYear));
        var user = await api.SignInAsync("investor", ct);
        var (aapl, petr4) = await HoldBothAsync(api, user.Id, ct);

        var usd = (await user.Client.GetFromJsonAsync<ReturnsBody>($"/api/returns/assets/{aapl.Id}", ct))!;
        var brl = (await user.Client.GetFromJsonAsync<ReturnsBody>($"/api/returns/assets/{petr4.Id}", ct))!;

        // 1000 USD at 5.0 = 5000 BRL; 1100 USD at 5.5 = 6050 BRL. +10% twice is +21%.
        Assert.Equal(new PeriodBody(Bought, MidYear, 179), usd.Period);
        Assert.Equal(new FxBody(0.1m, 0.1m, 0.21m), usd.Fx);
        Assert.Equal(0.21m, usd.Twr!.Total);
        Near(0.47505480744019315495m, usd.Twr.Annualised);

        // One buy and the closing value: the money's return is the asset's, so no timing effect.
        Near(0.47505480744019315495m, usd.Xirr);
        Near(0m, usd.TimingEffect);
        Assert.Equal(0.1m, usd.Benchmarks["USDBRL"]!.Total);

        // Only its own rows: the BRL asset stood still, and has no FX split.
        Assert.Null(brl.Fx);
        Assert.Equal(0m, brl.Twr!.Total);
    }

    [Fact]
    public async Task An_unknown_asset_is_a_404_and_the_query_is_validated_as_on_the_portfolio()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var api = await StartAsync(postgres, ct, ReturnsFixtures.ClockOn(MidYear));
        var user = await api.SignInAsync("investor", ct);
        var (aapl, _) = await HoldBothAsync(api, user.Id, ct);

        using var unknown = await user.Client.GetAsync($"/api/returns/assets/{Guid.NewGuid()}", ct);
        using var malformed = await user.Client.GetAsync($"/api/returns/assets/{aapl.Id}?period=weekly", ct);
        using var anonymous = await api.Factory.CreateApiClient().GetAsync($"/api/returns/assets/{aapl.Id}", ct);

        Assert.Equal(
            (HttpStatusCode.NotFound, HttpStatusCode.BadRequest, HttpStatusCode.Unauthorized),
            (unknown.StatusCode, malformed.StatusCode, anonymous.StatusCode));
    }

    private static async Task<(Finance.Api.Domain.Investments.Asset Usd, Finance.Api.Domain.Investments.Asset Brl)> HoldBothAsync(
        InvestmentsApi api, Guid userId, CancellationToken ct)
    {
        await api.UsdBrlAsync(ct, (Bought, 5m), (MidYear, 5.5m));
        var aapl = await api.HoldAsync(userId, await api.CatalogueAsync("AAPL", ct, "USD"), ct, Buy(Bought, 10m, 100m));
        await api.DailyAsync(aapl, ct, (Bought, 10m, 100m, 5m, 5000m), (MidYear, 10m, 110m, 5.5m, 6050m));
        var petr4 = await api.HoldAsync(userId, await api.CatalogueAsync("PETR4", ct), ct, Buy(Bought, 100m, 10m));
        await api.DailyAsync(petr4, Bought, MidYear, 100m, 10m, ct);
        return (aapl, petr4);
    }

    private static void Near(decimal expected, decimal? actual)
    {
        Assert.NotNull(actual);
        Assert.InRange(actual.Value, expected - 1e-8m, expected + 1e-8m);
    }
}
