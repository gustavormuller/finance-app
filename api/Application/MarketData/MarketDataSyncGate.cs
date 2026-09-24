namespace Finance.Api.Application.MarketData;

/// <summary>
/// One market-data sync at a time in the process: the manual trigger and the nightly job
/// both hold it for the whole run, so they never overlap. A singleton. The API is one
/// process (ADR-004), so an in-process semaphore is enough; a second instance would need
/// a PostgreSQL advisory lock instead. Not disposable on purpose: a run still in the
/// background when the container is disposed must still be able to release it.
/// </summary>
public sealed class MarketDataSyncGate
{
    private readonly SemaphoreSlim semaphore = new(1, 1);

    /// <summary>Takes the gate if it is free, without waiting. The manual trigger refuses rather than queues.</summary>
    public bool TryEnter() => semaphore.Wait(0);

    /// <summary>Waits for the gate. The nightly job runs after a manual run in progress, not instead of it.</summary>
    public Task WaitAsync(CancellationToken cancellationToken) => semaphore.WaitAsync(cancellationToken);

    public void Release() => semaphore.Release();
}
