using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Application.Transactions;

/// <summary>A transaction as a client writes it, to create one or to replace one.</summary>
public sealed record TransactionInput(
    Guid AccountId,
    Guid CategoryId,
    decimal Amount,
    string Currency,
    DateOnly Date,
    string Description);

/// <summary>The account and category a valid input names, or the rules it broke.</summary>
public sealed record TransactionResolution(Account? Account, Category? Category, IReadOnlyList<RuleViolation> Violations);

/// <summary>
/// Spec rules 1 to 5 for a transaction write. The two ids are resolved first because the
/// rules after them are questions about the rows they name; the pure rules are
/// <see cref="TransactionRules"/>'.
/// </summary>
/// <remarks>
/// Resolution goes through the query filter, so an id belonging to another user comes
/// back as null and is reported as a field that does not resolve. That is spec rule 4,
/// and it is why this is a 400 rather than a 403 — and why no write can follow it.
/// </remarks>
public sealed class TransactionInputRules(AppDbContext database)
{
    public async Task<TransactionResolution> ResolveAsync(TransactionInput input, CancellationToken cancellationToken)
    {
        var account = await database.Accounts
            .SingleOrDefaultAsync(entity => entity.Id == input.AccountId, cancellationToken);

        var category = await database.Categories
            .SingleOrDefaultAsync(entity => entity.Id == input.CategoryId, cancellationToken);

        if (account is null || category is null)
        {
            return Refused(
                account is null ? new RuleViolation("accountId", "Conta não encontrada.") : null,
                category is null ? new RuleViolation("categoryId", "Categoria não encontrada.") : null);
        }

        if (string.IsNullOrWhiteSpace(input.Description))
        {
            return Refused(new RuleViolation("description", "A descrição é obrigatória."));
        }

        // Checked before Money is constructed, so a bad code is a named field rather
        // than the constructor's ArgumentException turning into a 500.
        if (!Money.IsIsoCode(input.Currency))
        {
            return Refused(new RuleViolation("currency", "A moeda deve ser um código ISO 4217 de três letras maiúsculas."));
        }

        if (input.Currency != account.Currency)
        {
            return Refused(new RuleViolation(
                "currency",
                $"'{account.Name}' está em {account.Currency}, então o lançamento não pode estar em {input.Currency}."));
        }

        // Rounded first, so an amount of 0.001 is rejected as the zero it becomes
        // rather than accepted as the non-zero it arrived as.
        var amount = new Money(input.Amount, input.Currency).Amount;

        var violations = Refused(
            TransactionRules.ValidateAmount(amount),
            TransactionRules.ValidateSign(amount, category.Kind),
            TransactionRules.ValidateDate(input.Date, DateOnly.FromDateTime(DateTime.UtcNow)));

        return violations.Violations.Count == 0 ? new TransactionResolution(account, category, []) : violations;
    }

    private static TransactionResolution Refused(params RuleViolation?[] violations) =>
        new(null, null, [.. violations.OfType<RuleViolation>()]);
}
