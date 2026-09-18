using Finance.Api.Domain.Import;
using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// Shared setup for 004's persistence tests, which work against
/// <see cref="AppDbContext"/> directly: the guarantees under test — the query
/// filter, the partial indexes, the foreign keys — are the storage layer's.
/// </summary>
internal static class ImportFixtures
{
    public static ImportBatch ABatch(
        Guid userId,
        Guid accountId,
        ImportBatchStatus status = ImportBatchStatus.Staged,
        string fileName = "extrato.ofx") => new()
    {
        UserId = userId,
        AccountId = accountId,
        Source = ImportSource.Ofx,
        FileName = fileName,
        Status = status,
        RowCount = 0,
        CreatedAt = DateTimeOffset.UtcNow,
        CommittedAt = status == ImportBatchStatus.Committed ? DateTimeOffset.UtcNow : null,
        CommittedCount = status == ImportBatchStatus.Committed ? 0 : null,
    };

    public static StagedTransaction AStagedRow(
        Guid userId,
        Guid batchId,
        int rowNumber = 1,
        decimal? amount = -42.90m,
        string? externalId = null,
        StagedRowStatus status = StagedRowStatus.Ready) => new()
    {
        UserId = userId,
        ImportBatchId = batchId,
        RowNumber = rowNumber,
        Date = new DateOnly(2026, 9, 10),
        Amount = amount,
        Currency = Account.DefaultCurrency,
        RawDescription = "PAG*IFOOD 10/09",
        NormalizedDescription = "PAG IFOOD",
        ExternalId = externalId,
        Status = status,
        Included = status == StagedRowStatus.Ready,
    };

    public static CsvTemplate ATemplate(Guid userId, string name = "Nubank conta") => new()
    {
        UserId = userId,
        Name = name,
        Delimiter = ',',
        HasHeader = true,
        Culture = "en-US",
        DateFormat = "dd/MM/yyyy",
        SignMode = SignMode.Signed,
        DateColumn = "Data",
        AmountColumn = "Valor",
        DescriptionColumns = "Descrição",
        CreatedAt = DateTimeOffset.UtcNow,
    };

    /// <summary>An account and a category for <paramref name="userId"/>, written directly.</summary>
    public static async Task<(Guid AccountId, Guid CategoryId)> SeedAccountAndCategoryAsync(
        string connectionString,
        Guid userId,
        CancellationToken cancellationToken)
    {
        await using var context = TransactionsFixtures.ContextFor(connectionString, userId);

        var account = TransactionsFixtures.AnAccount(userId, "Conta " + Guid.NewGuid().ToString("N")[..8]);
        var category = TransactionsFixtures.ACategory(userId, "Categoria " + Guid.NewGuid().ToString("N")[..8]);

        context.Accounts.Add(account);
        context.Categories.Add(category);
        await context.SaveChangesAsync(cancellationToken);

        return (account.Id, category.Id);
    }
}
