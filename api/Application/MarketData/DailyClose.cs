namespace Finance.Api.Application.MarketData;

/// <summary>One day's close of an asset, as a price provider reports it. <c>decimal</c> from the parser on (006, test 9).</summary>
public readonly record struct DailyClose(DateOnly Date, decimal Close);
