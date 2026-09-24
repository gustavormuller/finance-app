namespace Finance.Api.Application.MarketData;

/// <summary>One day's value of a benchmark series, in the series' own unit.</summary>
public readonly record struct DailyValue(DateOnly Date, decimal Value);
