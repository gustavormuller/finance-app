namespace Finance.Api.Domain.Returns;

/// <summary>
/// One day of input to <see cref="TimeWeightedReturn"/>, all in one currency: the value at
/// the day's close, the income received that day, and the net external flow that day.
/// </summary>
/// <param name="Value"><c>V_d</c>: what the position or portfolio is worth at the close.</param>
/// <param name="Income"><c>D_d</c>: dividends and JCP received, net of their fees. Return, not a flow (decision 2).</param>
/// <param name="Flow"><c>F_d</c>: buys positive (cost plus fees), sells negative (proceeds less fees).</param>
public readonly record struct ReturnDay(DateOnly Date, decimal Value, decimal Income, decimal Flow);
