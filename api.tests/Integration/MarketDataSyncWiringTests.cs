using Finance.Api.Application.MarketData;
using Finance.Api.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 006 checkpoint 3 in the real host: the sync resolves from the container, and the test
/// hosts keep the nightly job off, so no integration test ever syncs against the network.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class MarketDataSyncWiringTests(PostgresFixture postgres)
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task The_test_hosts_resolve_the_sync_and_keep_the_nightly_job_off(bool identityHost)
    {
        var ct = TestContext.Current.CancellationToken;
        await using var identity = new IdentityApiFactory(postgres.ConnectionString);
        await using var health = new HealthApiFactory(postgres.ConnectionString);
        var services = identityHost ? identity.Services : health.Services;

        Assert.False(services.GetRequiredService<IOptions<MarketDataOptions>>().Value.ScheduledSync);
        var job = Assert.Single(services.GetServices<IHostedService>().OfType<MarketDataSyncJob>());
        await job.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), ct);
        Assert.True(job.ExecuteTask.IsCompletedSuccessfully);

        await using var scope = services.CreateAsyncScope();
        Assert.NotNull(scope.ServiceProvider.GetRequiredService<MarketDataSync>());
    }
}
