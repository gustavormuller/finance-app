using Finance.Api.Application;
using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Endpoints;

public static class AccountEndpoints
{
    private sealed record AccountRequest(string Name, AccountType Type, string? Currency);

    private sealed record AccountResponse(
        Guid Id,
        string Name,
        AccountType Type,
        string Currency,
        DateTimeOffset CreatedAt);

    public static IEndpointRouteBuilder MapAccountEndpoints(this IEndpointRouteBuilder routes)
    {
        var accounts = routes.MapGroup("/api/accounts").RequireAuthorization();

        // Ordered before projecting: EF cannot see through a constructor-only
        // projection to order by one of its arguments.
        accounts.MapGet("/", async (AppDbContext database, CancellationToken cancellationToken) =>
            Results.Ok(await database.Accounts
                .OrderBy(account => account.Name)
                .Select(account => new AccountResponse(
                    account.Id,
                    account.Name,
                    account.Type,
                    account.Currency,
                    account.CreatedAt))
                .ToListAsync(cancellationToken)));

        accounts.MapPost("/", async (
            AccountRequest request,
            AppDbContext database,
            ICurrentUser currentUser,
            CancellationToken cancellationToken) =>
        {
            if (Validate(request) is { } invalid)
            {
                return invalid;
            }

            var account = new Account
            {
                UserId = currentUser.Id!.Value,
                Name = request.Name.Trim(),
                Type = request.Type,
                Currency = request.Currency ?? Account.DefaultCurrency,
                CreatedAt = DateTimeOffset.UtcNow,
            };

            database.Accounts.Add(account);

            if (!await TrySaveAsync(database, cancellationToken))
            {
                return Problems.Conflict($"Já existe uma conta chamada '{account.Name}'.");
            }

            return Results.Created($"/api/accounts/{account.Id}", Describe(account));
        });

        accounts.MapPut("/{id:guid}", async (
            Guid id,
            AccountRequest request,
            AppDbContext database,
            CancellationToken cancellationToken) =>
        {
            if (Validate(request) is { } invalid)
            {
                return invalid;
            }

            // Through the query filter, so another user's id is simply not there.
            var account = await database.Accounts
                .SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);

            if (account is null)
            {
                return Results.NotFound();
            }

            account.Name = request.Name.Trim();
            account.Type = request.Type;
            account.Currency = request.Currency ?? account.Currency;

            if (!await TrySaveAsync(database, cancellationToken))
            {
                return Problems.Conflict($"Já existe uma conta chamada '{account.Name}'.");
            }

            return Results.Ok(Describe(account));
        });

        accounts.MapDelete("/{id:guid}", async (
            Guid id,
            AppDbContext database,
            CancellationToken cancellationToken) =>
        {
            var account = await database.Accounts
                .SingleOrDefaultAsync(entity => entity.Id == id, cancellationToken);

            if (account is null)
            {
                return Results.NotFound();
            }

            // Checked rather than left to the RESTRICT constraint, so the answer is a
            // sentence the UI can show instead of a constraint name. The constraint is
            // still there underneath, which is what makes this a courtesy.
            var referencing = await database.Transactions
                .CountAsync(transaction => transaction.AccountId == id, cancellationToken);

            if (referencing > 0)
            {
                return Problems.Conflict(
                    $"'{account.Name}' ainda tem {referencing} lançamento(s). "
                    + "Mova ou exclua os lançamentos antes de excluir a conta.");
            }

            database.Accounts.Remove(account);
            await database.SaveChangesAsync(cancellationToken);

            return Results.NoContent();
        });

        return routes;
    }

    private static IResult? Validate(AccountRequest request) =>
        Problems.Validation(
            string.IsNullOrWhiteSpace(request.Name)
                ? new RuleViolation("name", "O nome é obrigatório.")
                : null,
            request.Currency is not null && !Money.IsIsoCode(request.Currency)
                ? new RuleViolation("currency", "A moeda deve ser um código ISO 4217 de três letras maiúsculas.")
                : null);

    private static async Task<bool> TrySaveAsync(AppDbContext database, CancellationToken cancellationToken)
    {
        try
        {
            await database.SaveChangesAsync(cancellationToken);

            return true;
        }
        catch (DbUpdateException exception) when (exception.IsDuplicate())
        {
            return false;
        }
    }

    private static AccountResponse Describe(Account account) =>
        new(account.Id, account.Name, account.Type, account.Currency, account.CreatedAt);
}
