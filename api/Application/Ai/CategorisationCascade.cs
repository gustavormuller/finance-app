using Finance.Api.Domain.Ai;
using Finance.Api.Domain.Import;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Application.Ai;

/// <summary>What rung 3 did to a batch: rows whose category it changed, and rows sent that kept theirs.</summary>
public sealed record SuggestResult(int Suggested, int Skipped, ImportCommandProblem Problem);

/// <summary>
/// ADR-012 rung 3 (009, "CategorisationCascade — rung 3"): a staged batch's rows that fell to
/// the sign default go to the AI, and what it suggests replaces the default. Rungs 2 and the
/// default run at staging (<see cref="ImportStaging"/>).
/// </summary>
/// <remarks>
/// The AI only suggests: an answer is held to rule 3 (<see cref="AiCategorisation.Parse"/>),
/// the row stays staged, and the user confirms it at the commit. Rows go in batches of
/// <see cref="AiCategorisation.BatchSize"/>, one gateway call each, so each is budgeted, timed
/// out and recorded on its own. Each batch's suggestions are saved before the next call, both
/// because the gateway saves its usage row on this context and so that a failure keeps what
/// earlier batches suggested; asking again sends only the rows still on their default.
/// </remarks>
public sealed class CategorisationCascade(AppDbContext db, AiGateway gateway, ICurrentUser currentUser)
{
    /// <exception cref="AiDisabledException">The user has not turned AI on, checked even when nothing would be sent.</exception>
    /// <exception cref="AiBudgetExceededException">From the gateway, before a batch's call.</exception>
    /// <exception cref="AiProviderException">From the gateway; earlier batches' suggestions are kept.</exception>
    public async Task<SuggestResult> SuggestAsync(Guid batchId, CancellationToken ct)
    {
        var userId = currentUser.Id ?? throw new InvalidOperationException("A suggestion needs a user.");
        if (!await db.Users.Where(user => user.Id == userId).Select(user => user.AiEnabled).SingleOrDefaultAsync(ct))
        {
            throw new AiDisabledException();
        }

        var batch = await db.ImportBatches.SingleOrDefaultAsync(entity => entity.Id == batchId, ct);
        if (batch is null)
        {
            return new SuggestResult(0, 0, ImportCommandProblem.NotFound);
        }

        if (batch.Status != ImportBatchStatus.Staged)
        {
            return new SuggestResult(0, 0, ImportCommandProblem.WrongStatus);
        }

        var rows = await db.StagedTransactions
            .Where(row => row.ImportBatchId == batchId && row.CategorySource == CategorySource.Default)
            .Where(row => row.Amount != null && row.NormalizedDescription != null)
            .OrderBy(row => row.RowNumber)
            .ToListAsync(ct);
        if (rows.Count == 0)
        {
            return new SuggestResult(0, 0, ImportCommandProblem.None);
        }

        var (options, choices) = await CategoriesAsync(ct);
        var suggested = 0;
        foreach (var chunk in rows.Chunk(AiCategorisation.BatchSize))
        {
            var request = AiCategorisation.BuildRequest(
                [.. chunk.Select(row => new AiCategorisationRow(row.Id, row.NormalizedDescription!, row.Amount!.Value))],
                options);
            var completion = await gateway.CompleteAsync(
                AiPurpose.Categorisation, AiCategorisation.System, request, AiCategorisation.MaxTokensFor(chunk.Length), ct);

            var answer = AiCategorisation.Parse(completion.Text, chunk.ToDictionary(row => row.Id, row => row.Amount!.Value), choices);
            foreach (var row in chunk)
            {
                // The default itself is no suggestion: the row keeps its source and counts as skipped.
                if (answer.TryGetValue(row.Id, out var categoryId) && categoryId != row.CategoryId)
                {
                    row.CategoryId = categoryId;
                    row.CategorySource = CategorySource.Ai;
                    suggested++;
                }
            }

            // Not cancellable, like the usage row: the call was paid for.
            await db.SaveChangesAsync(CancellationToken.None);
        }

        return new SuggestResult(suggested, rows.Count - suggested, ImportCommandProblem.None);
    }

    /// <summary>The user's categories, a child named after its parent ("Alimentação > Padaria").</summary>
    private async Task<(AiCategoryOption[] Options, CategoryChoice[] Choices)> CategoriesAsync(CancellationToken ct)
    {
        var categories = await db.Categories
            .Select(category => new { category.Id, category.Name, category.Kind, category.ParentId })
            .ToListAsync(ct);
        var names = categories.ToDictionary(category => category.Id, category => category.Name);

        var options = categories
            .Select(category => new AiCategoryOption(
                category.Id,
                category.ParentId is { } parent && names.TryGetValue(parent, out var parentName) ? $"{parentName} > {category.Name}" : category.Name,
                category.Kind))
            .OrderBy(option => option.Name, StringComparer.Ordinal)
            .ToArray();
        return (options, [.. categories.Select(category => new CategoryChoice(category.Id, category.Kind))]);
    }
}
