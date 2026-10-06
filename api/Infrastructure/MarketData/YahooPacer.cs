namespace Finance.Api.Infrastructure.MarketData;

/// <summary>
/// Spaces the starts of Yahoo's requests (025, decision 3): Yahoo rate-limits clients that
/// call too fast, sometimes for hours, and asks nothing else in return for serving keyless.
/// One instance for the process, so a manual sync and the nightly one share the pace.
/// </summary>
public sealed class YahooPacer(TimeProvider clock)
{
    private readonly SemaphoreSlim turn = new(1, 1);

    private DateTimeOffset? lastStart;

    /// <summary>Returns once at least <paramref name="interval"/> has passed since the previous start.</summary>
    public async Task WaitTurnAsync(TimeSpan interval, CancellationToken cancellationToken)
    {
        await turn.WaitAsync(cancellationToken);
        try
        {
            if (lastStart is { } previous && previous + interval - clock.GetUtcNow() is var wait && wait > TimeSpan.Zero)
            {
                await Task.Delay(wait, clock, cancellationToken);
            }

            lastStart = clock.GetUtcNow();
        }
        finally
        {
            turn.Release();
        }
    }
}
