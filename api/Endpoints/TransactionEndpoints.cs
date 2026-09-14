using Finance.Api.Application;
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

    private sealed record TransactionRequest(
        Guid AccountId,
        Guid CategoryId,
        decimal Amount,
        string Currency,
        DateOnly Date,
        string Description);

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
            TransactionRequest request,
            AppDbContext database,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            var resolved = await ResolveAsync(request, database, cancellationToken);

            if (resolved.Problem is { } invalid)
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
            TransactionRequest request,
            AppDbContext database,
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

            var resolved = await ResolveAsync(request, database, cancellationToken);

            if (resolved.Problem is { } invalid)
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

    /// <summary>
    /// Spec rules 1 to 5. The two ids are resolved first because the rules after them
    /// are questions about the rows they name.
    /// </summary>
    /// <remarks>
    /// Resolution goes through the query filter, so an id belonging to another user
    /// comes back as null and is reported as a field that does not resolve. That is
    /// spec rule 4, and it is why this is a 400 rather than a 403 — and why no write
    /// can follow it.
    /// </remarks>
    private static async Task<(IResult? Problem, Account? Account, Category? Category)> ResolveAsync(
        TransactionRequest request,
        AppDbContext database,
        CancellationToken cancellationToken)
    {
        var account = await database.Accounts
            .SingleOrDefaultAsync(entity => entity.Id == request.AccountId, cancellationToken);

        var category = await database.Categories
            .SingleOrDefaultAsync(entity => entity.Id == request.CategoryId, cancellationToken);

        if (Problems.Validation(
                account is null ? new RuleViolation("accountId", "No such account.") : null,
                category is null ? new RuleViolation("categoryId", "No such category.") : null)
            is { } unresolved)
        {
            return (unresolved, null, null);
        }

        if (string.IsNullOrWhiteSpace(request.Description))
        {
            return (Problems.Validation("description", "Description is required."), null, null);
        }

        // Checked before Money is constructed, so a bad code is a named field rather
        // than the constructor's ArgumentException turning into a 500.
        if (!Money.IsIsoCode(request.Currency))
        {
            return (
                Problems.Validation(
                    "currency",
                    "Currency must be a three-letter uppercase ISO 4217 code."),
                null,
                null);
        }

        if (request.Currency != account!.Currency)
        {
            return (
                Problems.Validation(
                    "currency",
                    $"'{account.Name}' is in {account.Currency}, so this transaction cannot be "
                    + $"in {request.Currency}."),
                null,
                null);
        }

        // Rounded first, so an amount of 0.001 is rejected as the zero it becomes
        // rather than accepted as the non-zero it arrived as.
        var amount = new Money(request.Amount, request.Currency).Amount;

        var problem = Problems.Validation(
            TransactionRules.ValidateAmount(amount),
            TransactionRules.ValidateSign(amount, category!.Kind),
            TransactionRules.ValidateDate(request.Date, DateOnly.FromDateTime(DateTime.UtcNow)));

        return problem is null ? (null, account, category) : (problem, null, null);
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
