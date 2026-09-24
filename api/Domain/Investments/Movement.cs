namespace Finance.Api.Domain.Investments;

/// <summary>
/// One dated event on an <see cref="Asset"/>: a buy, a sell, a dividend, a JCP or a
/// split. A fact, above the line (ARCHITECTURE.md, system design): every position and
/// every <see cref="PortfolioDaily"/> row is derived from these.
/// </summary>
/// <remarks>
/// Quantities and unit prices are measurements with eight decimals, not
/// <see cref="Transactions.Money"/>, which pins scale to two (007, decision 10).
/// <see cref="Amount"/> and <see cref="Fees"/> are cash with two decimals, in
/// <see cref="Currency"/>; they share that one column, so they are plain decimals here
/// and become <c>Money</c> where they are added up.
/// </remarks>
public sealed class Movement : IUserOwned
{
    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public Guid AssetId { get; set; }

    /// <summary>The trade or payment day, not an instant.</summary>
    public DateOnly Date { get; set; }

    public MovementKind Kind { get; set; }

    /// <summary>Positive for Buy, Sell and Split; zero for Dividend and Jcp.</summary>
    public decimal Quantity { get; set; }

    /// <summary>Per unit, in the asset's currency; zero for Split, Dividend and Jcp.</summary>
    public decimal UnitPrice { get; set; }

    /// <summary>Cash received for a Dividend or Jcp, in the asset's currency; zero otherwise.</summary>
    public decimal Amount { get; set; }

    /// <summary>Brokerage and exchange fees, never negative.</summary>
    public decimal Fees { get; set; }

    /// <summary>Must equal the market asset's currency.</summary>
    public string Currency { get; set; } = "";

    public string? Notes { get; set; }

    /// <summary>Orders two movements on the same day (007, validation rules).</summary>
    public DateTimeOffset CreatedAt { get; set; }
}
