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

    public IReadOnlySet<string> ExternalIds { get; } =
        externalIds.Select(id => id.Trim()).ToHashSet(StringComparer.Ordinal);

    public IReadOnlySet<(DateOnly, decimal, string)> HeuristicKeys { get; } =
        heuristicKeys.Select(key => DuplicateMatcher.Key(key.Date, key.Amount, key.NormalizedDescription)).ToHashSet();
}

/// <summary>
/// Spec decision 7. <c>ExternalId</c> when the institution gave one, otherwise date +
/// amount + normalized description; within the batch and against what is already
/// committed.
/// </summary>
/// <remarks>
/// One pass over the rows with two hash sets, so a 5 000-row batch is linear. A row
/// with an id is judged on the id alone: two rows the bank numbered differently are
/// two transactions even when everything else matches, and a row the bank numbered
/// the same as one already committed is the same transaction even when the bank
/// rewrote its description.
/// </remarks>
public static class DuplicateMatcher
{
    public static IReadOnlySet<int> FindDuplicates(
        IReadOnlyList<DuplicateCandidate> rows,
        ExistingTransactionKeys existing)
    {
        var duplicates = new HashSet<int>();
        var seenExternal = new HashSet<string>(StringComparer.Ordinal);
        var seenHeuristic = new HashSet<(DateOnly, decimal, string)>();

        foreach (var row in rows)
        {
            if (!string.IsNullOrWhiteSpace(row.ExternalId))
            {
                var id = row.ExternalId.Trim();

                if (existing.ExternalIds.Contains(id) || !seenExternal.Add(id))
                {
                    duplicates.Add(row.Index);
                }

                continue;
            }

            // An incomplete key is an Invalid row, and calling it Duplicate as well
            // would hide the real problem.
            if (row.Date is not { } date
                || row.Amount is not { } amount
                || string.IsNullOrWhiteSpace(row.NormalizedDescription))
            {
                continue;
            }

            var key = Key(date, amount, row.NormalizedDescription);

            if (existing.HeuristicKeys.Contains(key) || !seenHeuristic.Add(key))
            {
                duplicates.Add(row.Index);
            }
        }

        return duplicates;
    }

    /// <summary>
    /// Rounded to the column's two places, so an amount read from a file and one read
    /// back from <c>numeric(18,2)</c> build the same key.
    /// </summary>
    internal static (DateOnly, decimal, string) Key(DateOnly date, decimal amount, string normalizedDescription) =>
        (date, Math.Round(amount, 2, MidpointRounding.ToEven), normalizedDescription.Trim());
}
