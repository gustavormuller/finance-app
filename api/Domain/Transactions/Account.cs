namespace Finance.Api.Domain.Transactions;

/// <summary>
/// Somewhere money sits: a bank account, a card, a wallet.
/// </summary>
/// <remarks>
/// Anaemic on purpose. A transaction has a date, an amount and a category, and there
/// is no invariant here worth an aggregate to protect (ADR-014, ADR-017).
/// </remarks>
public sealed class Account : IUserOwned
{
    /// <summary>
    /// The currency a new account gets when the caller does not name one. Only BRL
    /// exists in practice until the investments module.
    /// </summary>
    public const string DefaultCurrency = "BRL";

    public Guid Id { get; set; }

    public Guid UserId { get; set; }

    public string Name { get; set; } = "";

    public AccountType Type { get; set; }

    /// <summary>
    /// The currency of every transaction posted to this account. Held here rather
    /// than per transaction so a statement cannot end up mixing two currencies in one
    /// balance.
    /// </summary>
    public string Currency { get; set; } = DefaultCurrency;

    public DateTimeOffset CreatedAt { get; set; }
}
