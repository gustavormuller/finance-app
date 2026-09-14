using Finance.Api.Application;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Infrastructure;

/// <summary>
/// The single EF Core context. Per ADR-016 there is no repository layer over it:
/// <c>Application/</c> uses this type directly.
/// </summary>
/// <remarks>
/// Not sealed, and taking the non-generic <see cref="DbContextOptions"/>, so
/// <c>api.tests</c> can derive a context carrying one throwaway
/// <see cref="Domain.IUserOwned"/> entity and prove the generic query filter works
/// before any real entity depends on it.
/// </remarks>
public class AppDbContext(DbContextOptions options, ICurrentUser currentUser) : DbContext(options)
{
    /// <summary>
    /// The user every <see cref="Domain.IUserOwned"/> query is filtered by.
    /// </summary>
    protected ICurrentUser CurrentUser { get; } = currentUser;
}
