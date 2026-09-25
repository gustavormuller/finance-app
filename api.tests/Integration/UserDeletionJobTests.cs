using System.Collections.Concurrent;
using System.Net;
using Finance.Api.Application.Ai;
using Finance.Api.Application.MarketData;
using Finance.Api.Domain.Ai;
using Finance.Api.Domain.MarketData;
using Finance.Api.Infrastructure.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Npgsql;
using static Finance.Api.Tests.Integration.InvestmentsApi;
using static Finance.Api.Tests.Integration.TransactionsFixtures;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 023's spec tests 4-6: background work that finds its user gone stops quietly. It does not
/// crash the job, log an error, write a row, or count as a failure anyone else sees.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class UserDeletionJobTests(PostgresFixture postgres)
{
    /// <summary>Spec test 4.</summary>
    [Fact]
    public async Task A_pending_analysis_whose_user_is_gone_runs_nothing()
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = postgres.ConnectionString;
        var provider = new GatedAiProvider(gateFirstCall: false);
        await using var factory = new IdentityApiFactory(connection, services: services => services.AddSingleton<IAiProvider>(provider));
        var leaving = await EnabledUserAsync(factory, ct);
        var staying = await EnabledUserAsync(factory, ct);
        var pending = await AnalysisJobTests.PendingAsync(connection, leaving.Id, "2026-05", ct);
        using var deleted = await leaving.Client.SendAsync(Delete("/api/auth/me"), ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);

        Enqueue(factory, leaving.Id, pending);
        var marker = await AnalysisJobTests.PendingAsync(connection, staying.Id, "2026-05", ct);
        Enqueue(factory, staying.Id, marker);

        Assert.Equal(AiAnalysisStatus.Completed, (await AnalysisJobTests.SettledAsync(connection, staying.Id, marker, ct)).Status);
        Assert.Equal(1, provider.Calls);
    }

    /// <summary>Spec test 5: the call in flight finishes, and nothing is left to record it on.</summary>
    [Fact]
    public async Task A_running_analysis_whose_user_is_deleted_mid_call_stops_quietly_and_the_job_goes_on()
    {
        var ct = TestContext.Current.CancellationToken;
        var connection = postgres.ConnectionString;
        var provider = new GatedAiProvider(gateFirstCall: true);
        var logs = new CapturedLogs();
        await using var factory = new IdentityApiFactory(connection, services: services =>
        {
            services.AddSingleton<IAiProvider>(provider);
            services.AddSingleton<ILoggerProvider>(logs);
        });
        var leaving = await EnabledUserAsync(factory, ct);
        var staying = await EnabledUserAsync(factory, ct);
        var running = await AnalysisJobTests.PendingAsync(connection, leaving.Id, "2026-05", ct);

        Enqueue(factory, leaving.Id, running);
        await provider.FirstCallStarted.WaitAsync(TimeSpan.FromSeconds(20), ct);
        using var deleted = await leaving.Client.SendAsync(Delete("/api/auth/me"), ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        provider.ReleaseFirstCall();

        var next = await AnalysisJobTests.PendingAsync(connection, staying.Id, "2026-05", ct);
        Enqueue(factory, staying.Id, next);
        Assert.Equal(AiAnalysisStatus.Completed, (await AnalysisJobTests.SettledAsync(connection, staying.Id, next, ct)).Status);

        await using var all = ContextFor(connection, null);
        Assert.False(await all.AiAnalyses.IgnoreQueryFilters().AnyAsync(row => row.UserId == leaving.Id, ct));
        Assert.False(await all.AiUsage.IgnoreQueryFilters().AnyAsync(row => row.UserId == leaving.Id, ct));
        Assert.DoesNotContain(
            logs.Entries,
            entry => entry.Level >= LogLevel.Warning && entry.Category.StartsWith("Finance.Api", StringComparison.Ordinal));
        Assert.Contains(logs.Entries, entry => entry.Level == LogLevel.Information && entry.Message.Contains(running.ToString(), StringComparison.Ordinal));
    }

    /// <summary>
    /// Spec test 6. Both of the leaving user's assets are held under the rebuild's own advisory
    /// lock, so the run has listed them and waits on the first while the account goes; the
    /// second then finds nothing.
    /// </summary>
    [Fact]
    public async Task The_rebuild_after_a_sync_skips_a_user_deleted_mid_run_without_counting_a_failure()
    {
        var ct = TestContext.Current.CancellationToken;
        var day = Today.AddDays(-10);
        await using var api = await StartAsync(postgres, ct);
        var leaving = await api.SignInAsync("leaving", ct);
        var staying = await api.SignInAsync("staying", ct);
        var petr4 = await api.CatalogueAsync("PETR4", ct, "BRL", (day, 10m));
        var vale3 = await api.CatalogueAsync("VALE3", ct, "BRL", (day, 20m));
        var first = await api.HoldAsync(leaving.Id, petr4, ct, Buy(day, 100m, 10m));
        var second = await api.HoldAsync(leaving.Id, vale3, ct, Buy(day, 100m, 20m));
        var kept = await api.HoldAsync(staying.Id, petr4, ct, Buy(day, 100m, 10m));
        await using var all = api.Context(null);
        var run = new SyncRun
        {
            Id = Guid.NewGuid(),
            StartedAt = DateTimeOffset.UtcNow,
            FinishedAt = DateTimeOffset.UtcNow,
            Trigger = SyncTrigger.Scheduled,
            Status = SyncRunStatus.Succeeded,
            Summary = SyncSummaryJson.Write(new Dictionary<string, ProviderSyncSummary>()),
        };
        all.Add(run);
        await all.SaveChangesAsync(ct);

        await using var locks = new NpgsqlConnection(api.ConnectionString);
        await locks.OpenAsync(ct);
        await using var held = await locks.BeginTransactionAsync(ct);
        foreach (var asset in new[] { first, second })
        {
            await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(@key)", locks, held);
            command.Parameters.AddWithValue("key", RebuildLockKey(asset.Id));
            await command.ExecuteNonQueryAsync(ct);
        }

        var rebuild = RebuildAfterSyncAsync(api, run.Id, ct);
        await WaitForAnAdvisoryLockWaiterAsync(locks, ct);
        using var deleted = await leaving.Client.SendAsync(Delete("/api/auth/me"), ct);
        Assert.Equal(HttpStatusCode.NoContent, deleted.StatusCode);
        await held.CommitAsync(ct);
        await rebuild;

        var stored = await all.SyncRuns.AsNoTracking().SingleAsync(entity => entity.Id == run.Id, ct);
        var section = SyncSummaryJson.Read(stored.Summary)[SnapshotRebuildAfterSync.SummaryKey];
        Assert.Equal((0, null), (section.ItemsFailed, section.Error));
        Assert.Equal(SyncRunStatus.Succeeded, stored.Status);
        Assert.True(await all.PortfolioDaily.IgnoreQueryFilters().AnyAsync(row => row.AssetId == kept.Id, ct));
    }

    private static async Task<SignedInUser> EnabledUserAsync(IdentityApiFactory factory, CancellationToken ct)
    {
        var user = await factory.SignInNewUserAsync("delete-job", ct);
        using var turnedOn = await user.Client.SendAsync(ImportFixtures.Patch("/api/auth/me", new { aiEnabled = true }), ct);
        Assert.Equal(HttpStatusCode.OK, turnedOn.StatusCode);
        return user;
    }

    private static void Enqueue(IdentityApiFactory factory, Guid userId, Guid analysisId) =>
        factory.Services.GetRequiredService<AnalysisQueue>().Enqueue(userId, analysisId);

    private static async Task RebuildAfterSyncAsync(InvestmentsApi api, Guid syncRunId, CancellationToken ct)
    {
        await using var scope = api.Factory.Services.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<SnapshotRebuildAfterSync>().RunAsync(syncRunId, ct);
    }

    /// <summary>The key <c>SnapshotRebuild</c> locks an asset under: its id's first eight bytes.</summary>
    private static long RebuildLockKey(Guid assetId) => BitConverter.ToInt64(assetId.ToByteArray(), 0);

    private static async Task WaitForAnAdvisoryLockWaiterAsync(NpgsqlConnection connection, CancellationToken ct)
    {
        for (var attempt = 0; attempt < 400; attempt++)
        {
            await using var command = new NpgsqlCommand(
                "SELECT count(*) FROM pg_locks WHERE locktype = 'advisory' AND NOT granted "
                + "AND database = (SELECT oid FROM pg_database WHERE datname = current_database())",
                connection);
            if ((long)(await command.ExecuteScalarAsync(ct))! > 0)
            {
                return;
            }

            await Task.Delay(50, ct);
        }

        throw new TimeoutException("The rebuild never waited on the asset lock.");
    }

    /// <summary>Answers every call at once, except the first when gated: that one waits to be released.</summary>
    private sealed class GatedAiProvider(bool gateFirstCall) : IAiProvider
    {
        private readonly TaskCompletionSource started = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private readonly TaskCompletionSource gate = new(TaskCreationOptions.RunContinuationsAsynchronously);
        private int calls;

        public int Calls => Volatile.Read(ref calls);

        public Task FirstCallStarted => started.Task;

        public void ReleaseFirstCall() => gate.TrySetResult();

        public async Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct)
        {
            if (Interlocked.Increment(ref calls) == 1 && gateFirstCall)
            {
                started.TrySetResult();
                await gate.Task.WaitAsync(ct);
            }

            return new AiCompletion("## Resumo", 10, 10);
        }
    }

    private sealed class CapturedLogs : ILoggerProvider
    {
        private readonly ConcurrentQueue<(string Category, LogLevel Level, string Message)> entries = new();

        public IReadOnlyCollection<(string Category, LogLevel Level, string Message)> Entries => entries;

        public ILogger CreateLogger(string categoryName) => new Logger(categoryName, entries);

        public void Dispose()
        {
        }

        private sealed class Logger(string category, ConcurrentQueue<(string, LogLevel, string)> entries) : ILogger
        {
            public IDisposable? BeginScope<TState>(TState state)
                where TState : notnull => null;

            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter) =>
                entries.Enqueue((category, logLevel, formatter(state, exception)));
        }
    }
}
