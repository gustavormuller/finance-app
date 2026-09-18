using Finance.Api.Domain.Import;
using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Application;

/// <summary>Why a command could not run against the batch it named.</summary>
public enum ImportCommandProblem
{
    None = 0,

    /// <summary>No such batch under this user. Somebody else's batch looks exactly like this.</summary>
    NotFound = 1,

    /// <summary>The batch is not in the status the command needs.</summary>
    WrongStatus = 2,
}

public sealed record CommitResult(int Committed, int Skipped, ImportCommandProblem Problem);

public sealed record UndoResult(int Deleted, ImportCommandProblem Problem);

/// <summary>
/// The three things that can happen to a batch after staging. Each is one database
/// transaction, explicit, with no cascade doing work on its behalf.
/// </summary>
public sealed class ImportCommands(AppDbContext database)
{
    /// <summary>The <c>varchar(300)</c> of <see cref="Transaction.Description"/>.</summary>
    private const int DescriptionLength = 300;

    /// <summary>
    /// Every included row becomes a <see cref="Transaction"/>; the batch becomes
    /// Committed; the staged rows are deleted. Invalid and excluded rows are skipped
    /// and counted.
    /// </summary>
    /// <remarks>
    /// An included row whose ExternalId is already in the account — a duplicate the
    /// user chose to keep, or a bank that reused a FITID — is written without the id.
    /// The user asserted it is a separate movement, so the bank's id was not an id,
    /// and the partial unique index must stay true.
    /// </remarks>
    public async Task<CommitResult> CommitAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var batch = await database.ImportBatches.SingleOrDefaultAsync(entity => entity.Id == batchId, cancellationToken);

        if (batch is null)
        {
            return new CommitResult(0, 0, ImportCommandProblem.NotFound);
        }

        if (batch.Status != ImportBatchStatus.Staged)
        {
            return new CommitResult(0, 0, ImportCommandProblem.WrongStatus);
        }

        var account = await database.Accounts.SingleAsync(entity => entity.Id == batch.AccountId, cancellationToken);

        var rows = await database.StagedTransactions
            .Where(row => row.ImportBatchId == batchId)
            .OrderBy(row => row.RowNumber)
            .ToListAsync(cancellationToken);

        var included = rows
            .Where(row => row.Included && row.Status != StagedRowStatus.Invalid)
            .Where(row => row.Date is not null && row.Amount is not null && row.CategoryId is not null)
            .ToList();

        var batchExternalIds = included.Where(row => row.ExternalId is not null).Select(row => row.ExternalId!).Distinct().ToList();

        var usedExternalIds = await database.Transactions
            .Where(transaction => transaction.AccountId == account.Id)
            .Where(transaction => transaction.ExternalId != null && batchExternalIds.Contains(transaction.ExternalId))
            .Select(transaction => transaction.ExternalId!)
            .ToHashSetAsync(cancellationToken);

        var now = DateTimeOffset.UtcNow;

        var transactions = included.Select(row => new Transaction
        {
            UserId = batch.UserId,
            AccountId = account.Id,
            CategoryId = row.CategoryId!.Value,
            Money = new Money(row.Amount!.Value, account.Currency),
            Date = row.Date!.Value,
            Description = Truncate(row.RawDescription.Trim()),
            CreatedAt = now,
            ImportBatchId = batch.Id,
            ExternalId = row.ExternalId is { } id && usedExternalIds.Add(id) ? id : null,
            NormalizedDescription = row.NormalizedDescription,
        }).ToList();

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        database.Transactions.AddRange(transactions);
        batch.Status = ImportBatchStatus.Committed;
        batch.CommittedCount = transactions.Count;
        batch.CommittedAt = now;
        await database.SaveChangesAsync(cancellationToken);

        await database.StagedTransactions
            .Where(row => row.ImportBatchId == batchId)
            .ExecuteDeleteAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new CommitResult(transactions.Count, rows.Count - transactions.Count, ImportCommandProblem.None);
    }

    /// <summary>A staged batch and its rows, gone. The rows go by CASCADE; nothing else references a staged batch.</summary>
    public async Task<ImportCommandProblem> DiscardAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var batch = await database.ImportBatches.SingleOrDefaultAsync(entity => entity.Id == batchId, cancellationToken);

        if (batch is null)
        {
            return ImportCommandProblem.NotFound;
        }

        if (batch.Status != ImportBatchStatus.Staged)
        {
            return ImportCommandProblem.WrongStatus;
        }

        database.ImportBatches.Remove(batch);
        await database.SaveChangesAsync(cancellationToken);

        return ImportCommandProblem.None;
    }

    /// <summary>
    /// Delete the transactions the batch wrote, then the batch. Two explicit steps in
    /// one database transaction, not a cascade: an accidental batch delete must not
    /// silently remove transactions. Rows the user already deleted by hand are simply
    /// not there to delete.
    /// </summary>
    public async Task<UndoResult> UndoAsync(Guid batchId, CancellationToken cancellationToken)
    {
        var batch = await database.ImportBatches.SingleOrDefaultAsync(entity => entity.Id == batchId, cancellationToken);

        if (batch is null)
        {
            return new UndoResult(0, ImportCommandProblem.NotFound);
        }

        if (batch.Status != ImportBatchStatus.Committed)
        {
            return new UndoResult(0, ImportCommandProblem.WrongStatus);
        }

        await using var transaction = await database.Database.BeginTransactionAsync(cancellationToken);

        var deleted = await database.Transactions
            .Where(entity => entity.ImportBatchId == batchId)
            .ExecuteDeleteAsync(cancellationToken);

        database.ImportBatches.Remove(batch);
        await database.SaveChangesAsync(cancellationToken);

        await transaction.CommitAsync(cancellationToken);

        return new UndoResult(deleted, ImportCommandProblem.None);
    }

    private static string Truncate(string description) =>
        description.Length <= DescriptionLength ? description : description[..DescriptionLength].TrimEnd();
}
