using Finance.Api.Domain.Investments;
using Finance.Api.Domain.Transactions;
using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Application.Investments;

/// <summary>A movement as a client writes it. <c>Currency</c> defaults to the asset's.</summary>
public sealed record MovementInput(
    DateOnly Date,
    MovementKind Kind,
    decimal Quantity,
    decimal UnitPrice,
    decimal Amount,
    decimal Fees,
    string? Currency,
    string? Notes);

public enum MovementOutcome
{
    Done,
    NotFound,
    Invalid,
}

/// <summary>What a write did: the movement written, or why not.</summary>
public sealed record MovementWrite(MovementOutcome Outcome, Movement? Movement = null, IReadOnlyList<RuleViolation>? Violations = null);

/// <summary>
/// Movement writes (007): each one validated against the rules and the asset's whole
/// history, then saved and followed by <see cref="SnapshotRebuild"/> from
/// <c>min(oldDate, newDate)</c>, in one transaction, synchronously (decision 9).
/// </summary>
/// <remarks>
/// Fields that mean nothing for the kind are stored as zero, as the data model says:
/// quantity and unit price on a Dividend or Jcp, unit price on a Split, amount on a Buy,
/// Sell or Split. The calculator already ignores them; zeroing keeps the rows honest.
/// </remarks>
public sealed class MovementCommands(AppDbContext db, SnapshotRebuild rebuild, TimeProvider clock)
{
    private const int NotesLength = 300;

    public async Task<MovementWrite> AddAsync(Guid assetId, MovementInput input, CancellationToken cancellationToken)
    {
        if (await AssetAsync(assetId, cancellationToken) is not { } asset)
        {
            return new MovementWrite(MovementOutcome.NotFound);
        }

        var movement = new Movement { Id = Guid.NewGuid(), UserId = asset.UserId, AssetId = assetId, CreatedAt = clock.GetUtcNow() };
        var history = await HistoryAsync(assetId, cancellationToken);
        return await WriteAsync(movement, input, asset.Currency, [.. history, movement], input.Date, cancellationToken, isNew: true);
    }

    public async Task<MovementWrite> UpdateAsync(Guid movementId, MovementInput input, CancellationToken cancellationToken)
    {
        var movement = await db.Movements.SingleOrDefaultAsync(entity => entity.Id == movementId, cancellationToken);
        if (movement is null)
        {
            return new MovementWrite(MovementOutcome.NotFound);
        }

        var asset = (await AssetAsync(movement.AssetId, cancellationToken))!.Value;
        var history = await HistoryAsync(movement.AssetId, cancellationToken);
        var oldDate = movement.Date;
        return await WriteAsync(
            movement, input, asset.Currency, [.. history.Where(other => other.Id != movementId), movement], oldDate, cancellationToken, isNew: false);
    }

    /// <summary>Refused when it would leave a later sell uncovered, as the oversell rule says.</summary>
    public async Task<MovementWrite> DeleteAsync(Guid movementId, CancellationToken cancellationToken)
    {
        var movement = await db.Movements.SingleOrDefaultAsync(entity => entity.Id == movementId, cancellationToken);
        if (movement is null)
        {
            return new MovementWrite(MovementOutcome.NotFound);
        }

        var history = await HistoryAsync(movement.AssetId, cancellationToken);
        if (MovementRules.ValidatePositions(history.Where(other => other.Id != movementId)) is { } oversold)
        {
            return new MovementWrite(MovementOutcome.Invalid, movement, [oversold]);
        }

        db.Movements.Remove(movement);
        await SaveAndRebuildAsync(movement.AssetId, movement.Date, cancellationToken);
        return new MovementWrite(MovementOutcome.Done, movement);
    }

    private async Task<MovementWrite> WriteAsync(
        Movement movement, MovementInput input, string assetCurrency, List<Movement> after, DateOnly oldDate,
        CancellationToken cancellationToken, bool isNew)
    {
        if (!Enum.IsDefined(input.Kind))
        {
            return Invalid(new RuleViolation("kind", "O tipo de movimentação não é válido."));
        }

        var income = input.Kind is MovementKind.Dividend or MovementKind.Jcp;
        movement.Date = input.Date;
        movement.Kind = input.Kind;
        movement.Quantity = income ? 0m : input.Quantity;
        movement.UnitPrice = income || input.Kind == MovementKind.Split ? 0m : input.UnitPrice;
        movement.Amount = income ? input.Amount : 0m;
        movement.Fees = input.Fees;
        movement.Currency = input.Currency?.Trim().ToUpperInvariant() ?? assetCurrency;
        movement.Notes = string.IsNullOrWhiteSpace(input.Notes) ? null : input.Notes.Trim();

        var violations = MovementRules.Validate(movement, assetCurrency, rebuild.Today).ToList();
        if (movement.Notes is { Length: > NotesLength })
        {
            violations.Add(new RuleViolation("notes", $"As observações devem ter até {NotesLength} caracteres."));
        }

        if (violations.Count == 0 && MovementRules.ValidatePositions(after) is { } oversold)
        {
            violations.Add(oversold);
        }

        if (violations.Count > 0)
        {
            // An update's tracked entity must not be saved half-edited by a later save.
            db.ChangeTracker.Clear();
            return new MovementWrite(MovementOutcome.Invalid, Violations: violations);
        }

        if (isNew)
        {
            db.Movements.Add(movement);
        }

        await SaveAndRebuildAsync(movement.AssetId, oldDate < movement.Date ? oldDate : movement.Date, cancellationToken);
        return new MovementWrite(MovementOutcome.Done, movement);
    }

    private async Task SaveAndRebuildAsync(Guid assetId, DateOnly from, CancellationToken cancellationToken)
    {
        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        await db.SaveChangesAsync(cancellationToken);
        await rebuild.RebuildAsync(assetId, from, cancellationToken);
        await transaction.CommitAsync(cancellationToken);
    }

    /// <summary>The asset's owner and currency, or <c>null</c> when it is not the current user's.</summary>
    private async Task<(Guid UserId, string Currency)?> AssetAsync(Guid assetId, CancellationToken cancellationToken)
    {
        var asset = await (
                from held in db.Assets
                join market in db.MarketAssets on held.MarketAssetId equals market.Id
                where held.Id == assetId
                select new { held.UserId, market.Currency })
            .SingleOrDefaultAsync(cancellationToken);
        return asset is null ? null : (asset.UserId, asset.Currency);
    }

    private Task<List<Movement>> HistoryAsync(Guid assetId, CancellationToken cancellationToken) =>
        db.Movements.AsNoTracking().Where(movement => movement.AssetId == assetId).ToListAsync(cancellationToken);

    private static MovementWrite Invalid(RuleViolation violation) => new(MovementOutcome.Invalid, Violations: [violation]);
}
