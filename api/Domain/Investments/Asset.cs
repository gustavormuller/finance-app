namespace Finance.Api.Domain.Investments;

/// <summary>
/// A user's position in one instrument of the shared 006 catalogue. Ticker, name,
/// class and currency live on the <see cref="MarketData.MarketAsset"/> row and are
/// read through it, so two people holding PETR4 share one price series.
/// </summary>
/// <remarks>
/// No quantity column: the position on any date is derived from the
/// <see cref="Movement"/> rows up to that date (ADR-006). Unique by
/// <c>(UserId, MarketAssetId)</c>, one position per user per instrument.
/// </remarks>
public sealed class Asset : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid MarketAssetId { get; set; }

    /// <summary>An optional name the user gives the position, e.g. "Previdencia".</summary>
    public string? Nickname { get; set; }

    public DateTimeOffset CreatedAt { get; set; }
}
