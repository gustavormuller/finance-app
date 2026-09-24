using System.Threading.Channels;
using Cronos;
using Finance.Api.Application.MarketData;
using Finance.Api.Infrastructure.Jobs;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Time.Testing;

namespace Finance.Api.Tests.Unit;

/// <summary>
/// 006 spec tests 15 and 16: when the nightly job runs. Time is a
/// <see cref="FakeTimeProvider"/> in a UTC-3 zone, so nothing sleeps and the result does
/// not depend on the machine's zone.
/// </summary>
public sealed class MarketDataSyncJobTests
{
    private static readonly CronExpression Nightly = CronExpression.Parse("0 3 * * *");

    private static readonly TimeZoneInfo SaoPaulo =
        TimeZoneInfo.CreateCustomTimeZone("UTC-3", TimeSpan.FromHours(-3), "UTC-3", "UTC-3");

    /// <summary>04:00 local on 24 September, 07:00 UTC.</summary>
    private static readonly DateTimeOffset FourAm = new(2026, 9, 24, 7, 0, 0, TimeSpan.Zero);

    /// <summary>Spec test 15.</summary>
    [Fact]
    public void At_four_the_next_run_is_tomorrow_at_three_local_time()
    {
        Assert.Equal(
            new DateTimeOffset(2026, 9, 25, 3, 0, 0, TimeSpan.FromHours(-3)),
            MarketDataSyncJob.NextOccurrence(Nightly, FourAm, SaoPaulo));
        Assert.Equal(
            new DateTimeOffset(2026, 9, 24, 3, 0, 0, TimeSpan.FromHours(-3)),
            MarketDataSyncJob.NextOccurrence(Nightly, FourAm.AddHours(-2), SaoPaulo));
    }

    /// <summary>Spec test 16: the box was down at 3 a.m.</summary>
    [Theory]
    [InlineData(27.0)]
    [InlineData(null)]
    public async Task At_startup_a_latest_run_older_than_26_hours_or_none_runs_at_once(double? hoursAgo)
    {
        var (clock, runs, job) = Job();
        using var stop = new CancellationTokenSource();

        var loop = job.RunScheduleAsync(Nightly, Latest(clock, hoursAgo), Record(runs), stop.Token);

        Assert.Equal(1, runs.Reader.Count);
        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loop);
    }

    [Fact]
    public async Task At_startup_a_recent_run_waits_for_the_schedule()
    {
        var (clock, runs, job) = Job();
        using var stop = new CancellationTokenSource();

        var loop = job.RunScheduleAsync(Nightly, Latest(clock, 25.9), Record(runs), stop.Token);
        Assert.Equal(0, runs.Reader.Count);

        clock.Advance(TimeSpan.FromHours(22) + TimeSpan.FromMinutes(59));
        Assert.Equal(0, runs.Reader.Count);

        clock.Advance(TimeSpan.FromMinutes(1));
        await NextRunAsync(runs);

        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loop);
    }

    [Fact]
    public async Task A_failing_run_does_not_stop_the_schedule()
    {
        var (clock, runs, job) = Job();
        using var stop = new CancellationTokenSource();

        var loop = job.RunScheduleAsync(
            Nightly,
            Latest(clock, null),
            async ct =>
            {
                await Record(runs)(ct);
                throw new InvalidOperationException("database down");
            },
            stop.Token);

        clock.Advance(TimeSpan.FromDays(1));
        await NextRunAsync(runs);
        await NextRunAsync(runs);
        Assert.False(loop.IsCompleted);

        await stop.CancelAsync();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => loop);
    }

    /// <summary>What keeps the integration hosts and the E2E run off the network.</summary>
    [Fact]
    public async Task Switched_off_by_configuration_the_job_ends_without_running()
    {
        var (_, _, job) = Job(scheduledSync: false);

        await job.StartAsync(TestContext.Current.CancellationToken);

        await job.ExecuteTask!.WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);
        Assert.True(job.ExecuteTask.IsCompletedSuccessfully);
    }

    private static (FakeTimeProvider Clock, Channel<DateTimeOffset> Runs, MarketDataSyncJob Job) Job(bool scheduledSync = true)
    {
        var clock = new FakeTimeProvider(FourAm);
        clock.SetLocalTimeZone(SaoPaulo);
        var options = new MarketDataOptions { Schedule = "0 3 * * *", ScheduledSync = scheduledSync };

        // A container with nothing in it: a job that reached for the database would throw.
        var scopes = new ServiceCollection().BuildServiceProvider().GetRequiredService<IServiceScopeFactory>();
        var job = new MarketDataSyncJob(
            scopes, Microsoft.Extensions.Options.Options.Create(options), clock, NullLogger<MarketDataSyncJob>.Instance,
            new MarketDataSyncGate());
        return (clock, Channel.CreateUnbounded<DateTimeOffset>(), job);
    }

    private static Task<DateTimeOffset> NextRunAsync(Channel<DateTimeOffset> runs) =>
        runs.Reader.ReadAsync().AsTask().WaitAsync(TimeSpan.FromSeconds(10), TestContext.Current.CancellationToken);

    private static Func<CancellationToken, Task<DateTimeOffset?>> Latest(TimeProvider clock, double? hoursAgo) =>
        _ => Task.FromResult(hoursAgo is { } hours ? clock.GetUtcNow().AddHours(-hours) : (DateTimeOffset?)null);

    private static Func<CancellationToken, Task> Record(Channel<DateTimeOffset> runs) =>
        _ => runs.Writer.WriteAsync(DateTimeOffset.MinValue).AsTask();
}
