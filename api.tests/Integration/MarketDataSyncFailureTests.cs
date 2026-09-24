using System.Net;
using Finance.Api.Application.MarketData;
using Finance.Api.Domain.MarketData;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// Spec tests 12 to 14: failures are caught per asset and per provider, and the run's
/// status says how many got through. The summary's text is pt-BR, chosen by the
/// failure's type; the exception's English message never reaches it.
/// </summary>
public sealed partial class MarketDataSyncTests
{
    private const string ResponseInvalid = "O provedor respondeu em um formato inesperado.";

    private const string Unreachable = "Falha de comunicação com o provedor.";

    private const string RateLimited = "O provedor recusou por excesso de requisições; tente mais tarde.";

    /// <summary>Spec test 12.</summary>
    [Fact]
    public async Task One_failing_asset_does_not_stop_the_others_of_its_provider()
    {
        var ct = TestContext.Current.CancellationToken;
        var failing = await AssetAsync(ProviderKind.Brapi, "PETR4", ct);
        var working = await AssetAsync(ProviderKind.Brapi, "VALE3", ct);
        brapi.Respond = (symbol, _, to) => symbol == "PETR4"
            ? throw new ProviderResponseInvalidException("Brapi", "results is missing")
            : [new(to, 60m)];

        var run = await SyncAsync(ct);

        Assert.Equal(SyncRunStatus.PartialFailure, run.Status);
        await using var db = Context();
        Assert.Equal([working.Id], await db.Set<Price>().Select(price => price.MarketAssetId).ToListAsync(ct));
        Assert.Null((await db.Set<MarketAsset>().SingleAsync(asset => asset.Id == failing.Id, ct)).LastSyncedAt);
        Assert.Equal(Now, (await db.Set<MarketAsset>().SingleAsync(asset => asset.Id == working.Id, ct)).LastSyncedAt);

        var summary = SyncSummaryJson.Read(run.Summary)["Brapi"];
        Assert.Equal((1, 1, 1), (summary.RowsWritten, summary.ItemsSynced, summary.ItemsFailed));
        Assert.Equal([new SyncFailure("PETR4", ResponseInvalid)], summary.Failures);
        Assert.Equal(ResponseInvalid, summary.Error);
    }

    /// <summary>Spec test 13.</summary>
    [Fact]
    public async Task One_failing_provider_does_not_stop_the_others()
    {
        var ct = TestContext.Current.CancellationToken;
        await AssetAsync(ProviderKind.CoinGecko, "bitcoin", ct);
        await AssetAsync(ProviderKind.CoinGecko, "ethereum", ct);
        await AssetAsync(ProviderKind.Brapi, "PETR4", ct);
        coinGecko.Respond = (_, _, _) =>
            throw new HttpRequestException("Response status code does not indicate success: 503.", null, HttpStatusCode.ServiceUnavailable);

        var run = await SyncAsync(ct);

        Assert.Equal(SyncRunStatus.PartialFailure, run.Status);
        var summary = SyncSummaryJson.Read(run.Summary);
        Assert.Equal((0, 2), (summary["CoinGecko"].ItemsSynced, summary["CoinGecko"].ItemsFailed));
        Assert.Equal(Unreachable, summary["CoinGecko"].Error);
        Assert.Equal(1, summary["Brapi"].RowsWritten);
        Assert.Equal(1, summary["Bcb"].RowsWritten);
    }

    /// <summary>Spec test 14, first half.</summary>
    [Fact]
    public async Task Every_item_succeeding_is_Succeeded()
    {
        var ct = TestContext.Current.CancellationToken;
        await AssetAsync(ProviderKind.Brapi, "PETR4", ct);
        await AssetAsync(ProviderKind.CoinGecko, "bitcoin", ct);
        await AssetAsync(ProviderKind.TwelveData, "AAPL", ct);

        var run = await SyncAsync(ct);

        Assert.Equal(SyncRunStatus.Succeeded, run.Status);
        Assert.All(SyncSummaryJson.Read(run.Summary).Values, provider => Assert.Equal((1, 0), (provider.RowsWritten, provider.ItemsFailed)));
        await using var db = Context();
        Assert.Equal(3, await db.Set<Price>().CountAsync(ct));
    }

    /// <summary>Spec test 14, second half.</summary>
    [Fact]
    public async Task Every_item_failing_is_Failed()
    {
        var ct = TestContext.Current.CancellationToken;
        await AssetAsync(ProviderKind.Brapi, "PETR4", ct);
        await AssetAsync(ProviderKind.TwelveData, "AAPL", ct);
        brapi.Respond = (_, _, _) => throw new ProviderResponseInvalidException("Brapi", "bad");
        twelveData.Respond = (_, _, _) => throw new TaskCanceledException("The request timed out.");
        bcb.Respond = (_, _, _) => throw new HttpRequestException("Connection refused");

        var run = await SyncAsync(ct);

        Assert.Equal(SyncRunStatus.Failed, run.Status);
        Assert.Equal(Now, run.FinishedAt);
        Assert.Equal("O provedor não respondeu a tempo.", SyncSummaryJson.Read(run.Summary)["TwelveData"].Error);
    }

    /// <summary>A <c>429</c> means back off: the provider's remaining assets wait for the next run.</summary>
    [Fact]
    public async Task A_rate_limited_provider_is_not_asked_again_in_the_same_run()
    {
        var ct = TestContext.Current.CancellationToken;
        await AssetAsync(ProviderKind.Brapi, "PETR4", ct);
        await AssetAsync(ProviderKind.Brapi, "VALE3", ct);
        brapi.Respond = (_, _, _) => throw new ProviderRateLimitedException("Brapi", TimeSpan.FromMinutes(1));

        var run = await SyncAsync(ct);

        Assert.Equal(["PETR4"], brapi.Calls.Select(call => call.Symbol));
        Assert.Equal(SyncRunStatus.PartialFailure, run.Status);
        Assert.Equal(
            [new SyncFailure("PETR4", RateLimited), new SyncFailure("VALE3", RateLimited)],
            SyncSummaryJson.Read(run.Summary)["Brapi"].Failures);
    }
}
