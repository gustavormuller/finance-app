namespace Finance.Api.Domain.Returns;

/// <summary>
/// One day's value in a series: a benchmark's stored value, a position's value, or an
/// index. A measurement, so a raw <c>decimal</c> and not <see cref="Transactions.Money"/>.
/// </summary>
public readonly record struct DailyPoint(DateOnly Date, decimal Value);
