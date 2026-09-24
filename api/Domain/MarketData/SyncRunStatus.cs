namespace Finance.Api.Domain.MarketData;

/// <summary>How a <see cref="SyncRun"/> ended, or that it has not. Stored as <c>int</c>, values written down.</summary>
public enum SyncRunStatus
{
    Running = 0,
    Succeeded = 1,
    PartialFailure = 2,
    Failed = 3,
}
