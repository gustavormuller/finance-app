namespace Finance.Api.Application.Ai;

/// <summary>Loads <see cref="AnalysisAggregates"/> for the scope's user.</summary>
public sealed class AnalysisInputQueries
{
    public Task<AnalysisAggregates> AggregatesAsync(string month, CancellationToken ct) => throw new NotImplementedException();
}
