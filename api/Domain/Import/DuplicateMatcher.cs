namespace Finance.Api.Domain.Import;

/// <summary>One row of a batch, reduced to what dedupe looks at.</summary>
public sealed record DuplicateCandidate(
    int Index,
    string? ExternalId,
    DateOnly? Date,
    decimal? Amount,
    string? NormalizedDescription);

/// <summary>
/// The keys of the transactions already committed to the target account, fetched in
/// one query by the caller so matching itself needs no database.
/// </summary>
public sealed class ExistingTransactionKeys(
    IEnumerable<string> externalIds,
    IEnumerable<(DateOnly Date, decimal Amount, string NormalizedDescription)> heuristicKeys)
{
    public static ExistingTransactionKeys Empty { get; } = new([], []);

    public IReadOnlySet<string> ExternalIds { get; } = externalIds.ToHashSet(StringComparer.Ordinal);

    public IReadOnlySet<(DateOnly, decimal, string)> HeuristicKeys { get; } = heuristicKeys.ToHashSet();
}

/// <summary>
/// Spec decision 7. <c>ExternalId</c> when the institution gave one, otherwise date +
/// amount + normalized description; within the batch and against what is already
/// committed.
/// </summary>
public static class DuplicateMatcher
{
    public static IReadOnlySet<int> FindDuplicates(
        IReadOnlyList<DuplicateCandidate> rows,
        ExistingTransactionKeys existing) =>
        throw new NotImplementedException();
}
