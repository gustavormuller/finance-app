using Finance.Api.Domain.Import;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Tests.Integration;

/// <summary>
/// 009 checkpoint 4a: which rung chose a staged row's category (<see cref="CategorySource"/>),
/// the half of spec test 18 that staging owns.
/// </summary>
[Collection(nameof(PostgresCollection))]
public sealed class AiCategorisationTests(PostgresFixture postgres)
{
    [Fact]
    public async Task Staging_records_history_for_a_remembered_merchant_default_for_the_rest_and_none_for_an_invalid_row()
    {
        var ct = TestContext.Current.CancellationToken;
        await using var factory = new IdentityApiFactory(postgres.ConnectionString);
        var user = await factory.SignInNewUserAsync("ai-source", ct);
        var accountId = await user.CreateAccountAsync("Nubank", ct);
        var first = await user.UploadOfxAsync(
            accountId, ImportFixtures.Ofx([new ImportFixtures.OfxRow("20260901", "-42.90", "S-1", "PAG*IFOOD 01/09")]), ct);
        await user.CommitAsync(first.BatchId, ct);

        var second = await user.UploadOfxAsync(
            accountId,
            ImportFixtures.Ofx(
            [
                new ImportFixtures.OfxRow("20260915", "-58.00", "S-2", "PAG*IFOOD 15/09"),
                new ImportFixtures.OfxRow("20260915", "-19.90", "S-3", "PADARIA REAL"),
                new ImportFixtures.OfxRow("20260915", "3000.00", "S-4", "TED RECEBIDA"),
                new ImportFixtures.OfxRow("20260915", "0.00", "S-5", "ESTORNO"),
            ]),
            ct);

        Assert.Equal(
            [CategorySource.History, CategorySource.Default, CategorySource.Default, CategorySource.None],
            await SourcesAsync(user.Id, second.BatchId, ct));
    }

    private async Task<List<CategorySource>> SourcesAsync(Guid userId, Guid batchId, CancellationToken ct)
    {
        await using var context = TransactionsFixtures.ContextFor(postgres.ConnectionString, userId);
        return await context.StagedTransactions
            .Where(row => row.ImportBatchId == batchId)
            .OrderBy(row => row.RowNumber)
            .Select(row => row.CategorySource)
            .ToListAsync(ct);
    }
}
