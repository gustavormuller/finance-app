using System.Net;
using System.Net.Http.Json;
using Finance.Api.Application.MarketData;
using Finance.Api.Domain.MarketData;
using Finance.Api.Infrastructure.MarketData;
using Microsoft.Extensions.DependencyInjection;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 006 checkpoint 5: <c>MarketData:FakeProviders</c>, the switch the E2E run uses so its
/// real API process syncs without the network (spec E2E test 26). It is refused outside
/// Development, and it drops the manual-sync window to zero so every E2E run, and every
/// rerun within ten minutes, can trigger its sync on the shared database.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class MarketDataFakeProvidersTests(PostgresFixture postgres)
{
    private static readonly Dictionary<string, string?> FakesOn = new() { ["MarketData:FakeProviders"] = "true" };

    private sealed record Accepted(Guid SyncRunId);

    private sealed record SyncRunItem(Guid Id, string Trigger, string Status, Dictionary<string, SummaryPart> Summary);

    private sealed record SummaryPart(int RowsWritten, int ItemsSynced, int ItemsFailed, string? Error);

    [Fact]
    public async Task With_the_switch_a_manual_sync_succeeds_on_fakes_and_can_run_again_at_once()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new IdentityApiFactory(await postgres.CreateEmptyDatabaseAsync(ct), settings: FakesOn);
        var client = (await factory.SignInNewUserAsync("market-fakes", ct)).Client;
        using var created = await client.SendAsync(
            TransactionsFixtures.Post("/api/market-data/assets", MarketDataApi.Petr4()), ct);
        Assert.Equal(HttpStatusCode.Created, created.StatusCode);

        var first = await SyncAsync(client, ct);
        var second = await SyncAsync(client, ct);

        Assert.All([first, second], run => Assert.Equal(("Manual", "Succeeded"), (run.Trigger, run.Status)));
        Assert.Equal(2, first.Summary["Brapi"].ItemsSynced); // PETR4 and the IVVB11 benchmark
        Assert.True(first.Summary["Brapi"].RowsWritten > 0);
        Assert.True(first.Summary["Bcb"].ItemsSynced > 0);
    }

    [Fact]
    public async Task Without_the_switch_the_real_providers_are_registered()
    {
        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        await using var scope = factory.Services.CreateAsyncScope();

        var registry = scope.ServiceProvider.GetRequiredService<IPriceProviderRegistry>();

        Assert.IsType<BrapiProvider>(registry.For(ProviderKind.Brapi));
        Assert.IsType<BcbSgsProvider>(scope.ServiceProvider.GetRequiredService<IBenchmarkProvider>());
    }

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public async Task The_switch_fails_the_boot_outside_development(string environment)
    {
        await using var factory = new IdentityApiFactory(
            postgres.ConnectionString, environment: environment, settings: FakesOn);

        var error = Assert.ThrowsAny<Exception>(() => factory.Services);

        Assert.Contains("MarketData:FakeProviders", Flatten(error));
    }

    private static string Flatten(Exception error) =>
        error.InnerException is null ? error.Message : error.Message + " | " + Flatten(error.InnerException);

    private static async Task<SyncRunItem> SyncAsync(HttpClient client, CancellationToken ct)
    {
        using var response = await client.SendAsync(TransactionsFixtures.Post("/api/market-data/sync", new { }), ct);
        Assert.Equal(HttpStatusCode.Accepted, response.StatusCode);
        var id = (await response.Content.ReadFromJsonAsync<Accepted>(ct))!.SyncRunId;

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
