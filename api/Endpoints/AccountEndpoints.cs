using Finance.Api.Application;
using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Endpoints;

public static class AccountEndpoints
{
    /// <remarks>
    /// <c>OpeningBalance</c> (005 amendment 3) is optional: omitted on create it is
    /// zero, omitted on update it keeps the stored value, as <c>Currency</c> does.
    /// </remarks>
    private sealed record AccountRequest(
        string Name,
        AccountType Type,
        string? Currency,
        decimal? OpeningBalance);

    private sealed record AccountResponse(
        Guid Id,
        string Name,
        AccountType Type,
        string Currency,
        DateTimeOffset CreatedAt,
        decimal OpeningBalance);

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
                    account.CreatedAt,
                    account.OpeningBalance))
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
                OpeningBalance = Round(request.OpeningBalance ?? 0m),
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
            account.OpeningBalance = request.OpeningBalance is { } opening
                ? Round(opening)
                : account.OpeningBalance;

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

            // Checked rather than left to the RESTRICT constraints, so the answer is a
            // sentence the UI can show instead of a constraint name. The constraints are
            // still there underneath, which is what makes this a courtesy. A batch
            // counts whatever its status (003 amendment 1): one with no transactions
            // left holds the account just the same.
            var transactions = await database.Transactions
                .CountAsync(transaction => transaction.AccountId == id, cancellationToken);
            var imports = await database.ImportBatches
                .CountAsync(batch => batch.AccountId == id, cancellationToken);

            if (DeleteRefusal(account.Name, transactions, imports) is { } refusal)
            {
                return Problems.Conflict(refusal);
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

    /// <summary>
    /// Why the account cannot go yet, or null when nothing holds it. Both obstacles in
    /// one sentence, imports first: undoing an import takes its transactions with it.
    /// </summary>
    private static string? DeleteRefusal(string name, int transactions, int imports) =>
        (transactions, imports) switch
        {
            (0, 0) => null,
            (_, 0) => $"'{name}' ainda tem {transactions} lançamento(s). "
                + "Mova ou exclua os lançamentos antes de excluir a conta.",
            (0, _) => $"'{name}' ainda tem {imports} importação(ões) no histórico. "
                + "Desfaça ou descarte as importações antes de excluir a conta.",
            _ => $"'{name}' ainda tem {transactions} lançamento(s) e {imports} importação(ões) no histórico. "
                + "Desfaça ou descarte as importações e mova ou exclua os lançamentos restantes antes de excluir a conta.",
        };

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
        new(account.Id, account.Name, account.Type, account.Currency, account.CreatedAt, account.OpeningBalance);

    /// <summary>
    /// Rounded the way <see cref="Money"/> rounds, so the response says what
    /// <c>numeric(18,2)</c> will actually hold.
    /// </summary>
    private static decimal Round(decimal amount) =>
        decimal.Round(amount, 2, MidpointRounding.ToEven);
}
