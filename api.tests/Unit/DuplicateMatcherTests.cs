using Finance.Api.Domain.Import;

namespace Finance.Api.Tests.Unit;

/// <summary>Spec unit tests 29-32.</summary>
public sealed class DuplicateMatcherTests
{
    private static readonly DateOnly Day = new(2026, 9, 10);

    private static ExistingTransactionKeys Existing(
        IEnumerable<string>? externalIds = null,
        IEnumerable<(DateOnly, decimal, string)>? heuristic = null) =>
        new(externalIds ?? [], heuristic ?? []);

    private static DuplicateCandidate Row(
        int index,
        string? externalId = null,
        DateOnly? date = null,
        decimal? amount = -42.90m,
        string? normalized = "PAG IFOOD") =>
        new(index, externalId, date ?? Day, amount, normalized);

    /// <summary>
    /// Spec unit test 29, both directions. With an id, the id decides: a row whose
    /// id is already committed is a duplicate even if nothing else matches, and a
    /// row whose id is new is not a duplicate even when date, amount and description
    /// all match — that is two coffees, and the bank numbered them separately.
    /// </summary>
    [Fact]
    public void ExternalId_wins_over_the_heuristic()
    {
        var existing = Existing(
            externalIds: ["FIT-1"],
            heuristic: [(Day, -42.90m, "PAG IFOOD")]);

        var rows = new[]
        {
            Row(0, externalId: "FIT-1", amount: -999m, normalized: "SOMETHING ELSE"),
            Row(1, externalId: "FIT-2"),
        };

        var duplicates = DuplicateMatcher.FindDuplicates(rows, existing);

        Assert.Contains(0, duplicates);
        Assert.DoesNotContain(1, duplicates);
    }

    /// <summary>Spec unit test 30.</summary>
    [Fact]
    public void Without_an_id_date_amount_and_normalized_description_match()
    {
        var existing = Existing(heuristic: [(Day, -42.90m, "PAG IFOOD")]);

        var duplicates = DuplicateMatcher.FindDuplicates([Row(0)], existing);

        Assert.Contains(0, duplicates);
    }

    /// <summary>Spec unit test 31, and the other single-field differences.</summary>
    [Fact]
    public void One_field_different_is_not_a_duplicate()
    {
        var existing = Existing(heuristic: [(Day, -42.90m, "PAG IFOOD")]);

        var rows = new[]
        {
            Row(0, date: Day.AddDays(1)),
            Row(1, amount: -42.91m),
            Row(2, normalized: "PAG UBER"),
            Row(3, amount: 42.90m),
        };

        Assert.Empty(DuplicateMatcher.FindDuplicates(rows, existing));
    }

    /// <summary>Spec unit test 32: the second is marked, the first is not.</summary>
    [Fact]
    public void Identical_rows_in_one_batch_mark_every_copy_after_the_first()
    {
        var rows = new[] { Row(0), Row(1), Row(2), Row(3, amount: -1m) };

        var duplicates = DuplicateMatcher.FindDuplicates(rows, ExistingTransactionKeys.Empty);

        Assert.Equal([1, 2], duplicates.Order());
    }

    [Fact]
    public void Repeated_ids_in_one_batch_mark_every_copy_after_the_first()
    {
        // Banco do Brasil is known to repeat a FITID across distinct rows.
        var rows = new[]
        {
            Row(0, externalId: "SAME"),
            Row(1, externalId: "SAME", amount: -5m),
            Row(2, externalId: "OTHER"),
        };

        var duplicates = DuplicateMatcher.FindDuplicates(rows, ExistingTransactionKeys.Empty);

        Assert.Equal([1], duplicates.Order());
    }

    /// <summary>
    /// A row that failed to parse has no key. It is already Invalid, and calling it
    /// Duplicate as well would only hide the real problem.
    /// </summary>
    [Fact]
    public void Rows_without_a_complete_key_are_never_duplicates()
    {
        var rows = new[]
        {
            new DuplicateCandidate(0, null, Date: null, -42.90m, "PAG IFOOD"),
            new DuplicateCandidate(1, null, Date: null, -42.90m, "PAG IFOOD"),
            Row(2, amount: null),
            Row(3, amount: null),
            Row(4, normalized: ""),
            Row(5, normalized: ""),
            Row(6, normalized: null),
        };

        Assert.Empty(DuplicateMatcher.FindDuplicates(rows, ExistingTransactionKeys.Empty));
    }

    [Fact]
    public void Amounts_compare_by_value_not_by_scale()
    {
        var existing = Existing(heuristic: [(Day, -42.9m, "PAG IFOOD")]);

        Assert.Contains(0, DuplicateMatcher.FindDuplicates([Row(0, amount: -42.90m)], existing));
    }

    [Fact]
    public void An_empty_external_id_falls_back_to_the_heuristic()
    {
        var existing = Existing(heuristic: [(Day, -42.90m, "PAG IFOOD")]);

        var rows = new[] { Row(0, externalId: ""), Row(1, externalId: "   ") };

        Assert.Equal([0, 1], DuplicateMatcher.FindDuplicates(rows, existing).Order());
    }

    /// <summary>The upload limit is 5 000 rows; matching them must not be quadratic.</summary>
    [Fact]
    public void A_large_batch_is_matched_in_one_pass()
    {
        var rows = Enumerable.Range(0, 5_000)
            .Select(index => Row(index, externalId: $"FIT-{index % 2_500}"))
            .ToList();

        var duplicates = DuplicateMatcher.FindDuplicates(rows, ExistingTransactionKeys.Empty);

        Assert.Equal(2_500, duplicates.Count);
        Assert.All(duplicates, index => Assert.True(index >= 2_500));
    }
}
