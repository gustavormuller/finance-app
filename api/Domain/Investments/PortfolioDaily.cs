namespace Finance.Api.Domain.Investments;

/// <summary>
/// The valued position of one <see cref="Asset"/> on one calendar day. Derived, below
/// the line: it can be truncated and rebuilt from <see cref="Movement"/> rows and the
/// shared prices at any time with no loss (ADR-011). Never the source of truth.
/// </summary>
/// <remarks>
/// Keyed by <c>(UserId, AssetId, Date)</c>. One row per calendar day from the first
/// movement onward; weekends and holidays carry the last close forward, and
/// <see cref="PriceDate"/> says which day it is from.
/// </remarks>
public sealed class PortfolioDaily : IUserOwned
{
    public Guid UserId { get; set; }

    public Guid AssetId { get; set; }

    public DateOnly Date { get; set; }

    public decimal Quantity { get; set; }

    /// <summary>Per unit, in the asset's currency.</summary>
    public decimal AverageCost { get; set; }

    /// <summary>The close in the asset's currency, carried forward on non-trading days.</summary>
    public decimal Price { get; set; }

    /// <summary>The day <see cref="Price"/> was actually quoted.</summary>
    public DateOnly PriceDate { get; set; }

    /// <summary>BRL per unit of the asset's currency; <c>1</c> for BRL.</summary>
    public decimal FxRate { get; set; }

    /// <summary><c>Quantity x Price x FxRate</c>, two decimals.</summary>
    public decimal ValueBrl { get; set; }

    /// <summary>
    /// What the position cost in BRL, each buy at its own day's FX rate, reduced
    /// proportionally by sells. Not <c>Quantity x AverageCost x today's FxRate</c>.
    /// </summary>
    public decimal CostBasisBrl { get; set; }
}
