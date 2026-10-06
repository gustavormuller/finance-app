namespace Finance.Api.Application.MarketData;

/// <summary>One day's close of an asset, as a price provider reports it. <c>decimal</c> from the parser on (006, test 9).</summary>
/// <param name="Close">The raw traded price (<see cref="Domain.MarketData.Price.Close"/>).</param>
/// <param name="AdjustedClose">Adjusted for later splits and dividends; only Yahoo sends one (025).</param>
public readonly record struct DailyClose(DateOnly Date, decimal Close, decimal? AdjustedClose = null);
