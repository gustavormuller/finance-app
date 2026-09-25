using Finance.Api.Application;
using Finance.Api.Application.Transactions;
using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Endpoints;

public static class TransactionEndpoints
{
    /// <summary>
    /// The spec's ceiling. Clamped rather than refused: an oversized page is a caller
    /// being greedy, not a caller being wrong.
    /// </summary>
    private const int MaximumPageSize = 200;

    private const int DefaultPageSize = 50;

    private sealed record TransactionResponse(
        Guid Id,
        Guid AccountId,
        string AccountName,
        Guid CategoryId,
        string CategoryName,
        decimal Amount,
        string Currency,
        DateOnly Date,
        string Description,
        DateTimeOffset CreatedAt);

    private sealed record TransactionPage(
        IReadOnlyList<TransactionResponse> Items,
        int Page,
        int PageSize,
        int Total);

    public static IEndpointRouteBuilder MapTransactionEndpoints(this IEndpointRouteBuilder routes)
    {
        var transactions = routes.MapGroup("/api/transactions").RequireAuthorization();

        transactions.MapGet("/", async (
            AppDbContext database,
            CancellationToken cancellationToken,
            DateOnly? from = null,
            DateOnly? to = null,
            Guid? accountId = null,
            Guid? categoryId = null,
            Guid? importBatchId = null,
            int page = 1,
            int pageSize = DefaultPageSize) =>
        {
            page = Math.Max(page, 1);
            pageSize = Math.Clamp(pageSize, 1, MaximumPageSize);

            var query = database.Transactions.AsQueryable();

            // Both ends inclusive, which is what a person means by "March 10th to
            // March 20th" on a filter bar.
            if (from is { } start)
            {
                query = query.Where(transaction => transaction.Date >= start);
            }

            if (to is { } end)
            {
                query = query.Where(transaction => transaction.Date <= end);
            }

            if (accountId is { } onlyAccount)
            {
                query = query.Where(transaction => transaction.AccountId == onlyAccount);
            }

            if (categoryId is { } onlyCategory)
            {
                query = query.Where(transaction => transaction.CategoryId == onlyCategory);
            }

            // The "see what this import wrote" link from the done step.
            if (importBatchId is { } onlyBatch)
            {
                query = query.Where(transaction => transaction.ImportBatchId == onlyBatch);
            }

            var total = await query.CountAsync(cancellationToken);

            // Joined rather than navigated: the entities carry no navigation
            // properties, and each of the three sets brings its own query filter into
            // the join.
            var items = await (
                    from transaction in query
                    join account in database.Accounts on transaction.AccountId equals account.Id
                    join category in database.Categories on transaction.CategoryId equals category.Id
                    orderby transaction.Date descending, transaction.CreatedAt descending
                    select new TransactionResponse(
                        transaction.Id,
                        account.Id,
                        account.Name,
                        category.Id,
                        category.Name,
                        transaction.Money.Amount,
                        transaction.Money.Currency,
                        transaction.Date,
                        transaction.Description,
                        transaction.CreatedAt))
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync(cancellationToken);

            return Results.Ok(new TransactionPage(items, page, pageSize, total));
        });

        transactions.MapPost("/", async (
            TransactionInput request,
            AppDbContext database,
            TransactionInputRules rules,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            var resolved = await rules.ResolveAsync(request, cancellationToken);

            if (Problems.Validation(resolved.Violations) is { } invalid)
            {
                return invalid;
            }

            var transaction = new Transaction
            {
                UserId = currentUser.Id!.Value,
                AccountId = request.AccountId,
                CategoryId = request.CategoryId,
                Money = new Money(request.Amount, request.Currency),
                Date = request.Date,
                Description = request.Description.Trim(),
                CreatedAt = DateTimeOffset.UtcNow,
            };

            database.Transactions.Add(transaction);
            await database.SaveChangesAsync(cancellationToken);

            return Results.Created(
                $"/api/transactions/{transaction.Id}",
                Describe(transaction, resolved.Account!, resolved.Category!));
        });

        transactions.MapPut("/{id:guid}", async (
            Guid id,
            TransactionInput request,
            AppDbContext database,
            TransactionInputRules rules,
            CancellationToken cancellationToken) =>
        {
            // Before validating the body: somebody else's transaction is not there to
            // be corrected, whatever the body says.
            var transaction = await database.Transactions
                .SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);

            if (transaction is null)
            {
                return Results.NotFound();
            }

            var resolved = await rules.ResolveAsync(request, cancellationToken);

            if (Problems.Validation(resolved.Violations) is { } invalid)
            {
                return invalid;
            }

            transaction.AccountId = request.AccountId;
            transaction.CategoryId = request.CategoryId;
            transaction.Money = new Money(request.Amount, request.Currency);
            transaction.Date = request.Date;
            transaction.Description = request.Description.Trim();

            await database.SaveChangesAsync(cancellationToken);

            return Results.Ok(Describe(transaction, resolved.Account!, resolved.Category!));
        });

        transactions.MapDelete("/{id:guid}", async (
            Guid id,
            AppDbContext database,
            CancellationToken cancellationToken) =>
        {
            var transaction = await database.Transactions
                .SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);

            if (transaction is null)
            {
                return Results.NotFound();
            }

            // Hard delete. Undo is 004's problem, and only for imports.
            database.Transactions.Remove(transaction);
            await database.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        });

        return routes;
    }

    private static TransactionResponse Describe(
        Transaction transaction,
        Account account,
        Category category) =>
        new(
            transaction.Id,
            account.Id,
            account.Name,
            category.Id,
            category.Name,
            transaction.Money.Amount,
            transaction.Money.Currency,
            transaction.Date,
            transaction.Description,
            transaction.CreatedAt);
}
