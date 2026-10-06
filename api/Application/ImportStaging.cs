using System.Text.Json;
using Finance.Api.Domain.Import;
using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Npgsql;

namespace Finance.Api.Application;

/// <summary>What a staged batch looks like from the outside: the counts the preview opens with.</summary>
public sealed record StagingSummary(Guid BatchId, int RowCount, int Ready, int Duplicates, int Invalid);

/// <summary>
/// Either a staged batch, or the id of the batch that is already open and has to be
/// committed or discarded first.
/// </summary>
public sealed record StagingOutcome(StagingSummary? Summary, Guid? OpenBatchId);

/// <summary>
/// Parsed rows to staged rows: validate, normalize, dedupe, suggest, insert. The
/// synchronous half of ARCHITECTURE.md §2, in the request (spec decision 11).
/// </summary>
/// <remarks>
/// Three queries whatever the file size: the committed keys of the target account
/// for dedupe, the most recent category per normalized description for the history
/// rung, and the user's categories. Everything else is the pure functions of
/// <c>Domain/Import</c> over lists in memory, then one batched insert.
/// </remarks>
public sealed class ImportStaging(AppDbContext database)
{
    /// <summary>PostgreSQL's SQLSTATE for unique_violation.</summary>
    private const string UniqueViolation = "23505";

    public async Task<StagingOutcome> StageAsync(
        Guid userId,
        Account account,
        ImportSource source,
        string fileName,
        IReadOnlyList<ParsedRow> rows,
        CancellationToken cancellationToken)
    {
        var batch = new ImportBatch
        {
            // Assigned here rather than by EF on Add, so the rows can carry it before
            // anything is saved.
            Id = Guid.NewGuid(),
            UserId = userId,
            AccountId = account.Id,
            Source = source,
            FileName = fileName,
            Status = ImportBatchStatus.Staged,
            RowCount = rows.Count,
            CreatedAt = DateTimeOffset.UtcNow,
        };

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var staged = rows.Select(row => Validate(row, userId, batch.Id, account.Currency, today)).ToList();

        await MarkDuplicatesAsync(staged, account.Id, cancellationToken);
        await SuggestCategoriesAsync(staged, cancellationToken);

        database.ImportBatches.Add(batch);
        database.StagedTransactions.AddRange(staged);

        try
        {
            await database.SaveChangesAsync(cancellationToken);
        }
        catch (DbUpdateException exception)
            when (exception.InnerException is PostgresException { SqlState: UniqueViolation })
        {
            // The partial unique index on (UserId) where Status = Staged, doing the
            // job a pre-check could not do safely.
            database.ChangeTracker.Clear();

            var open = await database.ImportBatches
                .Where(existing => existing.Status == ImportBatchStatus.Staged)
                .Select(existing => existing.Id)
                .FirstOrDefaultAsync(cancellationToken);

            return new StagingOutcome(null, open);
        }

        return new StagingOutcome(
            new StagingSummary(
                batch.Id,
                staged.Count,
                staged.Count(row => row.Status == StagedRowStatus.Ready),
                staged.Count(row => row.Status == StagedRowStatus.Duplicate),
                staged.Count(row => row.Status == StagedRowStatus.Invalid)),
            null);
    }

    /// <summary>
    /// The spec's row-level checks, each appending its pt-BR message. Row-level,
    /// never file-level: three bad rows out of two hundred leave a hundred and
    /// ninety-seven Ready.
    /// </summary>
    private static StagedTransaction Validate(ParsedRow row, Guid userId, Guid batchId, string accountCurrency, DateOnly today)
    {
        var issues = new List<string>(row.Issues);

        // Rounded as Money would round it, so 0.001 is rejected as the zero it
        // becomes rather than stored as the non-zero it arrived as.
        decimal? amount = row.Amount is { } raw ? Math.Round(raw, 2, MidpointRounding.ToEven) : null;

        if (amount == 0m)
        {
            issues.Add(RowIssues.ZeroAmount);
        }

        if (row.Date is { } date && TransactionRules.ValidateDate(date, today) is not null)
        {
            issues.Add(RowIssues.DateOutOfRange);
        }

        if (row.Currency is { } currency && currency != accountCurrency)
        {
            issues.Add(RowIssues.CurrencyMismatch(currency));
        }

        var normalized = DescriptionNormalizer.Normalize(row.RawDescription);

        if (normalized.Length == 0)
        {
            issues.Add(RowIssues.EmptyDescription);
        }

        var valid = issues.Count == 0;

        return new StagedTransaction
        {
            UserId = userId,
            ImportBatchId = batchId,
            RowNumber = row.RowNumber,
            Date = row.Date,
            Amount = amount,
            Currency = row.Currency,
            RawDescription = row.RawDescription,
            NormalizedDescription = normalized.Length == 0 ? null : normalized,
            ExternalId = string.IsNullOrWhiteSpace(row.ExternalId) ? null : row.ExternalId.Trim(),
            Status = valid ? StagedRowStatus.Ready : StagedRowStatus.Invalid,
            Included = valid,
            Issues = valid ? null : JsonSerializer.Serialize(issues),
        };
    }

    /// <summary>
    /// Spec decision 7, against this account's committed transactions and within
    /// the batch. Only Ready rows take part: an Invalid row is already telling the
    /// user something more important.
    /// </summary>
    private async Task MarkDuplicatesAsync(List<StagedTransaction> staged, Guid accountId, CancellationToken cancellationToken)
    {
        var ready = staged.Where(row => row.Status == StagedRowStatus.Ready).ToList();

        if (ready.Count == 0)
        {
            return;
        }

        var externalIds = ready.Where(row => row.ExternalId is not null).Select(row => row.ExternalId!).Distinct().ToList();
        var dates = ready.Where(row => row.Date is not null).Select(row => row.Date!.Value).ToList();
        var first = dates.Count == 0 ? DateOnly.MaxValue : dates.Min();
        var last = dates.Count == 0 ? DateOnly.MinValue : dates.Max();

        var committed = await database.Transactions
            .Where(transaction => transaction.AccountId == accountId)
            .Where(transaction =>
                (transaction.ExternalId != null && externalIds.Contains(transaction.ExternalId))
                || (transaction.NormalizedDescription != null
                    && transaction.Date >= first
                    && transaction.Date <= last))
            .Select(transaction => new
            {
                transaction.ExternalId,
                transaction.Date,
                transaction.Money.Amount,
                transaction.NormalizedDescription,
            })
            .ToListAsync(cancellationToken);

        var existing = new ExistingTransactionKeys(
            committed.Where(key => key.ExternalId is not null).Select(key => key.ExternalId!),
            committed.Where(key => key.NormalizedDescription is not null)
                .Select(key => (key.Date, key.Amount, key.NormalizedDescription!)));

        var candidates = ready
            .Select((row, index) => new DuplicateCandidate(index, row.ExternalId, row.Date, row.Amount, row.NormalizedDescription))
            .ToList();

        foreach (var index in DuplicateMatcher.FindDuplicates(candidates, existing))
        {
            ready[index].Status = StagedRowStatus.Duplicate;
            ready[index].Included = false;
        }
    }

    /// <summary>
    /// ADR-012 rung 2 and the sign default. A row nothing resolves for becomes
    /// Invalid with "Categoria não encontrada", which only happens once the seeded
    /// defaults are gone.
    /// </summary>
    private async Task SuggestCategoriesAsync(List<StagedTransaction> staged, CancellationToken cancellationToken)
    {
        var rows = staged.Where(row => row.Status != StagedRowStatus.Invalid && row.Amount is not null).ToList();

        if (rows.Count == 0)
        {
            return;
        }

        var categories = await database.Categories
            .Select(category => new { category.Id, category.Kind, category.Name, category.ParentId })
            .ToListAsync(cancellationToken);

        var choices = categories.ToDictionary(category => category.Id, category => new CategoryChoice(category.Id, category.Kind));

        CategoryChoice? Default(string name) => categories
            .Where(category => category.ParentId == null && category.Name == name)
            .Select(category => choices[category.Id])
            .FirstOrDefault();

        var defaultExpense = Default(DefaultCategories.OtherExpenseName);
        var defaultIncome = Default(DefaultCategories.OtherIncomeName);

        var keys = rows.Select(row => row.NormalizedDescription!).Distinct().ToList();

        // The most recent committed transaction per normalized description, decided
        // in the database rather than by fetching every match.
        var history = await database.Transactions
            .Where(transaction => transaction.NormalizedDescription != null && keys.Contains(transaction.NormalizedDescription))
            .GroupBy(transaction => transaction.NormalizedDescription!)
            .Select(group => new
            {
                Key = group.Key,
                CategoryId = group
                    .OrderByDescending(transaction => transaction.Date)
                    .ThenByDescending(transaction => transaction.CreatedAt)
                    .Select(transaction => transaction.CategoryId)
                    .First(),
            })
            .ToDictionaryAsync(entry => entry.Key, entry => entry.CategoryId, cancellationToken);

        foreach (var row in rows)
        {
            var remembered = history.TryGetValue(row.NormalizedDescription!, out var categoryId)
                && choices.TryGetValue(categoryId, out var choice)
                ? choice
                : null;

            row.CategoryId = CategorySuggester.Suggest(row.Amount!.Value, remembered, defaultExpense, defaultIncome);

            // Rung 3 may only touch a row the sign default filed. A remembered category
            // wins even where it is the default itself: history chose it.
            row.CategorySource = row.CategoryId is null ? CategorySource.None
                : row.CategoryId == remembered?.Id ? CategorySource.History
                : CategorySource.Default;

            if (row.CategoryId is null)
            {
                row.Status = StagedRowStatus.Invalid;
                row.Included = false;
                row.Issues = JsonSerializer.Serialize(new[] { RowIssues.CategoryNotFound });
            }
        }
    }
}
