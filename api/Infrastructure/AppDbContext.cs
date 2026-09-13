using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Infrastructure;

/// <summary>
/// Deliberately empty. 001 is a walking skeleton: the point of the context and its
/// empty initial migration is to prove the EF Core pipeline reaches PostgreSQL before
/// anything depends on it.
/// </summary>
/// <remarks>
/// The first real tables arrive with Identity in 002. Per ADR-007, every domain entity
/// added here carries a <c>UserId</c> and a global query filter configured in
/// <see cref="DbContext.OnModelCreating"/>; <c>prices</c> and <c>benchmarks</c> are the
/// only exceptions, being shared market data.
/// </remarks>
public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options);
