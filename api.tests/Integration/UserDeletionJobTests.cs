using System.Collections.Concurrent;
using System.Net;
using Finance.Api.Application.Ai;
using Finance.Api.Domain.Ai;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using static Finance.Api.Tests.Integration.TransactionsFixtures;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 023's spec tests 4-5: background work that finds its user gone stops quietly. It does not
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

    private static async Task<SignedInUser> EnabledUserAsync(IdentityApiFactory factory, CancellationToken ct)
    {
        var user = await factory.SignInNewUserAsync("delete-job", ct);
        using var turnedOn = await user.Client.SendAsync(ImportFixtures.Patch("/api/auth/me", new { aiEnabled = true }), ct);
        Assert.Equal(HttpStatusCode.OK, turnedOn.StatusCode);
        return user;
    }

    private static void Enqueue(IdentityApiFactory factory, Guid userId, Guid analysisId) =>
        factory.Services.GetRequiredService<AnalysisQueue>().Enqueue(userId, analysisId);

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
