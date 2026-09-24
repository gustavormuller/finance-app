using System.Diagnostics;
using Finance.Api.Application.Ai;
using Finance.Api.Domain.Ai;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 009 checkpoint 3: the gateway times every provider call out by purpose, on its clock:
/// 30 s for categorisation (decision 4), <c>Ai:Analysis:TimeoutSeconds</c> for the analysis.
/// A timeout is <see cref="AiProviderTimeoutException"/> (CP4's <c>504</c>) and is recorded
/// with an estimate; the caller's own cancellation stays a cancellation.
/// </summary>
public sealed partial class AiGatewayTests
{
    private static readonly TimeSpan Guard = TimeSpan.FromSeconds(20);

    [Fact]
    public async Task A_categorisation_call_times_out_at_30_seconds_and_is_recorded_with_an_estimate()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = Clock();
        var provider = new HangingAiProvider();
        await using var host = await HostAsync(provider, clock, ct, ("Ai:Categorisation:TimeoutSeconds", "30"));
        var user = await EnabledUserAsync(host, ct);

        var call = CallAsync(host, user, AiPurpose.Categorisation, ct, system: new string('s', 40), prompt: new string('u', 40));
        await provider.Entered.Task.WaitAsync(Guard, ct);
        clock.Advance(TimeSpan.FromSeconds(29));
        Assert.False(call.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(1));

        var error = await Assert.ThrowsAsync<AiProviderTimeoutException>(() => call.WaitAsync(Guard, ct));

        Assert.Null(error.InputTokens);
        var row = Assert.Single(await UsageAsync(host.Connection, user, ct));
        Assert.Equal((false, 20, 0), (row.Succeeded, row.InputTokens, row.OutputTokens));
    }

    [Fact]
    public async Task An_analysis_call_has_its_own_longer_timeout()
    {
        var ct = TestContext.Current.CancellationToken;
        var clock = Clock();
        var provider = new HangingAiProvider();
        await using var host = await HostAsync(provider, clock, ct, ("Ai:Analysis:TimeoutSeconds", "120"));
        var user = await EnabledUserAsync(host, ct);

        var call = CallAsync(host, user, AiPurpose.Analysis, ct);
        await provider.Entered.Task.WaitAsync(Guard, ct);
        clock.Advance(TimeSpan.FromSeconds(119));
        Assert.False(call.IsCompleted);
        clock.Advance(TimeSpan.FromSeconds(1));

        await Assert.ThrowsAsync<AiProviderTimeoutException>(() => call.WaitAsync(Guard, ct));
    }

    [Fact]
    public async Task The_callers_cancellation_is_a_cancellation_not_a_timeout_and_is_still_recorded()
    {
        var ct = TestContext.Current.CancellationToken;
        var provider = new HangingAiProvider();
        await using var host = await HostAsync(provider, Clock(), ct);
        var user = await EnabledUserAsync(host, ct);
        using var caller = CancellationTokenSource.CreateLinkedTokenSource(ct);

        var call = CallAsync(host, user, AiPurpose.Categorisation, caller.Token);
        await provider.Entered.Task.WaitAsync(Guard, ct);
        await caller.CancelAsync();

        var error = await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call.WaitAsync(Guard, ct));

        Assert.IsNotType<AiProviderTimeoutException>(error);
        Assert.False(Assert.Single(await UsageAsync(host.Connection, user, ct)).Succeeded);
    }

    /// <summary>Signals once it is called, then waits for its token.</summary>
    private sealed class HangingAiProvider : IAiProvider
    {
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<AiCompletion> CompleteAsync(AiRequest request, CancellationToken ct)
        {
            Entered.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            throw new UnreachableException();
        }
    }
}
