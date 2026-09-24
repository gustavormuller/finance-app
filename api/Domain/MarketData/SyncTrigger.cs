namespace Finance.Api.Domain.MarketData;

/// <summary>What started a <see cref="SyncRun"/>. Stored as <c>int</c>, values written down.</summary>
public enum SyncTrigger
{
    Scheduled = 0,
    Manual = 1,
}
