using Finance.Api.Domain.Transactions;

namespace Finance.Api.Domain.Returns;

/// <summary>Money that crossed the boundary of a position on a day: a buy, a sell, a dividend.</summary>
public readonly record struct CashFlow(DateOnly Date, Money Amount);
