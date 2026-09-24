namespace Finance.Api.Application.Ai;

/// <summary>What rung 3 did to a batch: rows whose category it changed, and rows sent that kept theirs.</summary>
public sealed record SuggestResult(int Suggested, int Skipped, ImportCommandProblem Problem);

/// <summary>
/// ADR-012 rung 3 (009, "CategorisationCascade — rung 3"): a staged batch's rows that fell to
/// the sign default go to the AI, and what it suggests replaces the default. Rungs 2 and the
/// default run at staging (<see cref="ImportStaging"/>).
/// </summary>
public sealed class CategorisationCascade
{
    public Task<SuggestResult> SuggestAsync(Guid batchId, CancellationToken ct) =>
        throw new NotImplementedException();
}
