using Finance.Api.Infrastructure;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Application;

/// <summary>
/// 023's account deletion (ADR-013): everything the signed-in user owns, then the Identity
/// user itself, in one database transaction. Shared market data is not touched.
/// </summary>
/// <remarks>
/// <para>
/// The user is the scope's, never a parameter, so no caller can point this at someone else.
/// Every statement is scoped twice: by the query filter, which is that same user, and by
/// its own <c>UserId</c> predicate.
/// </para>
/// <para>
/// Children before parents. The keys between user tables are RESTRICT, and the cascades
/// from <c>AspNetUsers</c> fire in an order nobody chose, so they are not relied on. The
/// categories go in one statement: PostgreSQL checks their RESTRICT self-reference at the
/// end of it, when the children are already gone.
/// </para>
/// <para>
/// A new user-owned table fails <c>UserDeletionTests</c> until it is seeded there and
/// deleted here, or cascaded.
/// </para>
/// </remarks>
public sealed class UserDeletion(AppDbContext db, ICurrentUser currentUser)
{
    /// <returns>False when the session's user row no longer exists, which deletes nothing.</returns>
    public async Task<bool> DeleteAsync(CancellationToken ct)
    {
        var userId = currentUser.Id ?? throw new InvalidOperationException("Deleting an account needs a signed-in user.");

        await using var transaction = await db.Database.BeginTransactionAsync(ct);

        await db.StagedTransactions.Where(row => row.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Transactions.Where(row => row.UserId == userId).ExecuteDeleteAsync(ct);
        await db.ImportBatches.Where(row => row.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Accounts.Where(row => row.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Categories.Where(row => row.UserId == userId).ExecuteDeleteAsync(ct);
        await db.CsvTemplates.Where(row => row.UserId == userId).ExecuteDeleteAsync(ct);

        await db.PortfolioDaily.Where(row => row.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Movements.Where(row => row.UserId == userId).ExecuteDeleteAsync(ct);
        await db.Assets.Where(row => row.UserId == userId).ExecuteDeleteAsync(ct);

        await db.AiUsage.Where(row => row.UserId == userId).ExecuteDeleteAsync(ct);
        await db.AiAnalyses.Where(row => row.UserId == userId).ExecuteDeleteAsync(ct);

        await db.UserTokens.Where(row => row.UserId == userId).ExecuteDeleteAsync(ct);
        await db.UserLogins.Where(row => row.UserId == userId).ExecuteDeleteAsync(ct);
        await db.UserClaims.Where(row => row.UserId == userId).ExecuteDeleteAsync(ct);
        var users = await db.Users.Where(user => user.Id == userId).ExecuteDeleteAsync(ct);

        await transaction.CommitAsync(ct);
        return users == 1;
    }
}
