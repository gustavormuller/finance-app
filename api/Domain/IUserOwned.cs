namespace Finance.Api.Domain;

/// <summary>
/// Marks an entity as belonging to exactly one user.
/// </summary>
/// <remarks>
/// <see cref="Infrastructure.AppDbContext.OnModelCreating"/> applies a global query
/// filter to every entity type implementing this interface, in one loop rather than
/// one line per entity. Isolation is therefore a consequence of implementing the
/// interface, not of remembering to write a filter (ADR-007).
/// <para>
/// Shared market data — <c>prices</c>, <c>benchmarks</c> — deliberately does not
/// implement it.
/// </para>
/// </remarks>
public interface IUserOwned
{
    Guid UserId { get; }
}
