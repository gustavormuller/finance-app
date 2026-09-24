using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using Finance.Api.Application.MarketData;
using Finance.Api.Domain.MarketData;
using Microsoft.Extensions.DependencyInjection;
using static Finance.Api.Tests.Integration.MarketDataApi;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 006 checkpoint 4: the manual sync and the run history, spec integration tests 20 to 22,
/// with fake providers. The rate limit is global, so each test has a database of its own.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class MarketDataSyncEndpointTests(PostgresFixture postgres)
{
    private sealed record SummaryPart(int RowsWritten, int ItemsSynced, int ItemsFailed, string? Error);

    private sealed record SyncRunItem(
        Guid Id, DateTimeOffset StartedAt, DateTimeOffset? FinishedAt, string Trigger, string Status,
        Dictionary<string, SummaryPart> Summary);

    private sealed record Accepted(Guid SyncRunId);

    /// <summary>Spec integration test 20.</summary>
    [Fact]
    public async Task A_registered_asset_has_prices_after_a_manual_sync()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, client) = await StartAsync(postgres, ct);
        await using var _ = api;
        using var created = await client.SendAsync(TransactionsFixtures.Post("/api/market-data/assets", Petr4()), ct);
        var asset = (await created.Content.ReadFromJsonAsync<AssetItem>(ct))!;

        using var sync = await client.SendAsync(TransactionsFixtures.Post("/api/market-data/sync", new { }), ct);

        Assert.Equal(HttpStatusCode.Accepted, sync.StatusCode);
        var runId = (await sync.Content.ReadFromJsonAsync<Accepted>(ct))!.SyncRunId;
        var run = await FinishedAsync(client, runId, ct);
        Assert.Equal(("Manual", "Succeeded"), (run.Trigger, run.Status));
        Assert.Equal(2, run.Summary["Brapi"].ItemsSynced); // PETR4 and the IVVB11 benchmark
        Assert.Contains(api.Brapi.Calls, call => call.Symbol == "PETR4");

        var yesterday = DateOnly.FromDateTime(DateTime.UtcNow).AddDays(-1);
        var prices = await client.GetFromJsonAsync<List<PriceItem>>($"/api/market-data/assets/{asset.Id}/prices", ct);
        Assert.Equal([new PriceItem(yesterday, 10m)], prices);
    }

    /// <summary>Spec integration test 21.</summary>
    [Fact]
    public async Task A_second_manual_sync_within_ten_minutes_is_a_429()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, client) = await StartAsync(postgres, ct);
        await using var _ = api;

        using var first = await client.SendAsync(TransactionsFixtures.Post("/api/market-data/sync", new { }), ct);
        var runId = (await first.Content.ReadFromJsonAsync<Accepted>(ct))!.SyncRunId;
        await FinishedAsync(client, runId, ct);
        using var second = await client.SendAsync(TransactionsFixtures.Post("/api/market-data/sync", new { }), ct);

        Assert.Equal(HttpStatusCode.Accepted, first.StatusCode);
        Assert.Equal(HttpStatusCode.TooManyRequests, second.StatusCode);
        Assert.Equal("application/problem+json", second.Content.Headers.ContentType?.MediaType);
        using var problem = JsonDocument.Parse(await second.Content.ReadAsStringAsync(ct));
        Assert.Contains("10 minutos", problem.RootElement.GetProperty("detail").GetString());
        Assert.InRange(second.Headers.RetryAfter?.Delta?.TotalSeconds ?? 0, 1, 600);
        Assert.Single((await client.GetFromJsonAsync<List<SyncRunItem>>("/api/market-data/sync-runs", ct))!);
    }

    /// <summary>"If one ran in the last 10 minutes": any trigger counts, and the limit reads the database.</summary>
    [Theory]
    [InlineData(SyncTrigger.Scheduled, 5, HttpStatusCode.TooManyRequests)]
    [InlineData(SyncTrigger.Manual, 9, HttpStatusCode.TooManyRequests)]
    [InlineData(SyncTrigger.Manual, 11, HttpStatusCode.Accepted)]
    public async Task The_limit_counts_the_latest_run_of_any_trigger(SyncTrigger trigger, int minutesAgo, HttpStatusCode expected)
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, client) = await StartAsync(postgres, ct);
        await using var _ = api;
        await SeedRunsAsync(api, ct, (trigger, DateTimeOffset.UtcNow.AddMinutes(-minutesAgo)));

        using var response = await client.SendAsync(TransactionsFixtures.Post("/api/market-data/sync", new { }), ct);

        Assert.Equal(expected, response.StatusCode);
        if (expected == HttpStatusCode.Accepted)
        {
            await FinishedAsync(client, (await response.Content.ReadFromJsonAsync<Accepted>(ct))!.SyncRunId, ct);
        }
    }

    /// <summary>The nightly job holds the same gate, so a manual run never overlaps it.</summary>
    [Fact]
    public async Task A_manual_sync_while_another_run_holds_the_gate_is_a_429()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, client) = await StartAsync(postgres, ct);
        await using var _ = api;
        var gate = api.Factory.Services.GetRequiredService<MarketDataSyncGate>();
        Assert.True(gate.TryEnter());

        try
        {
            using var response = await client.SendAsync(TransactionsFixtures.Post("/api/market-data/sync", new { }), ct);
            Assert.Equal(HttpStatusCode.TooManyRequests, response.StatusCode);
        }
        finally
        {
            gate.Release();
        }

        Assert.Empty((await client.GetFromJsonAsync<List<SyncRunItem>>("/api/market-data/sync-runs", ct))!);
    }

    /// <summary>Spec integration test 22: the last 20, newest first, with the summary as an object.</summary>
    [Fact]
    public async Task Sync_runs_are_the_last_twenty_newest_first()
    {
        var ct = TestContext.Current.CancellationToken;
        var (api, client) = await StartAsync(postgres, ct);
        await using var _ = api;
        var start = new DateTimeOffset(2026, 8, 1, 3, 0, 0, TimeSpan.Zero);
        var days = Enumerable.Range(0, 25).OrderBy(day => (day * 7) % 25).ToList();
        await SeedRunsAsync(api, ct, [.. days.Select(day => (SyncTrigger.Scheduled, start.AddDays(day)))]);

        var runs = (await client.GetFromJsonAsync<List<SyncRunItem>>("/api/market-data/sync-runs", ct))!;
        using var anonymous = api.Factory.CreateApiClient();
        using var refused = await anonymous.GetAsync("/api/market-data/sync-runs", ct);

        Assert.Equal([.. Enumerable.Range(5, 20).Reverse().Select(day => start.AddDays(day))], runs.Select(run => run.StartedAt));
        Assert.Equal(("Scheduled", "PartialFailure"), (runs[0].Trigger, runs[0].Status));
        Assert.Equal((3, 1, "Limite de teste"), (runs[0].Summary["Brapi"].RowsWritten, runs[0].Summary["Brapi"].ItemsFailed, runs[0].Summary["Brapi"].Error));
        Assert.Equal(HttpStatusCode.Unauthorized, refused.StatusCode);
    }

    private static async Task SeedRunsAsync(MarketDataApi api, CancellationToken ct, params (SyncTrigger Trigger, DateTimeOffset StartedAt)[] runs)
    {
        await using var db = api.Context();
        var summary = SyncSummaryJson.Write(new Dictionary<string, ProviderSyncSummary>
        {
            ["Brapi"] = new() { RowsWritten = 3, ItemsSynced = 1, ItemsFailed = 1, Error = "Limite de teste" },
        });
        db.AddRange(runs.Select(run => new SyncRun
        {
            Id = Guid.NewGuid(), StartedAt = run.StartedAt, FinishedAt = run.StartedAt.AddMinutes(1),
            Trigger = run.Trigger, Status = SyncRunStatus.PartialFailure, Summary = summary,
        }));
        await db.SaveChangesAsync(ct);
    }

    /// <summary>The run happens after the 202; waits for it to leave <c>Running</c>.</summary>
    private static async Task<SyncRunItem> FinishedAsync(HttpClient client, Guid id, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            var runs = await client.GetFromJsonAsync<List<SyncRunItem>>("/api/market-data/sync-runs", ct);
            if (runs!.SingleOrDefault(run => run.Id == id) is { Status: not "Running" } run)
            {
                return run;
            }

            await Task.Delay(100, ct);
        }

        throw new TimeoutException($"Sync run {id} did not finish.");
    }
}
