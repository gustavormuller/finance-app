using System.Reflection;
using Finance.Api.Application;
using Finance.Api.Domain;
using Finance.Api.Domain.Identity;
using Finance.Api.Domain.Import;
using Finance.Api.Domain.MarketData;
using Finance.Api.Domain.Transactions;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Infrastructure;

/// <summary>
/// The single EF Core context. Per ADR-016 there is no repository layer over it:
/// <c>Application/</c> uses this type directly.
/// </summary>
/// <remarks>
/// <see cref="IdentityUserContext{TUser, TKey}"/> rather than <c>IdentityDbContext</c>:
/// four Identity tables instead of seven, because there are no roles to migrate or
/// review.
/// <para>
/// Not sealed, and taking the non-generic <see cref="DbContextOptions"/>, so
/// <c>api.tests</c> can derive a context carrying one throwaway
/// <see cref="IUserOwned"/> entity and prove the query filter below before any real
/// entity depends on it.
/// </para>
/// </remarks>
public class AppDbContext(DbContextOptions options, ICurrentUser currentUser)
    : IdentityUserContext<AppUser, Guid>(options)
{
    private static readonly MethodInfo ApplyUserFilterMethod =
        typeof(AppDbContext).GetMethod(
            nameof(ApplyUserFilter),
            BindingFlags.Instance | BindingFlags.NonPublic)!;

    /// <summary>
    /// The user every <see cref="IUserOwned"/> query is filtered by. Read through a
    /// member access on the context so EF Core turns it into a query parameter it
    /// re-evaluates per query, instead of baking the value into the cached model.
    /// </summary>
    protected ICurrentUser CurrentUser { get; } = currentUser;

    public DbSet<Account> Accounts => Set<Account>();

    public DbSet<Category> Categories => Set<Category>();

    public DbSet<Transaction> Transactions => Set<Transaction>();

    public DbSet<ImportBatch> ImportBatches => Set<ImportBatch>();

    public DbSet<StagedTransaction> StagedTransactions => Set<StagedTransaction>();

    public DbSet<CsvTemplate> CsvTemplates => Set<CsvTemplate>();

    public DbSet<MarketAsset> MarketAssets => Set<MarketAsset>();

    public DbSet<Price> Prices => Set<Price>();

    public DbSet<Benchmark> Benchmarks => Set<Benchmark>();

    public DbSet<SyncRun> SyncRuns => Set<SyncRun>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<AppUser>(user =>
        {
            // ADR-010: a row inserted by anything that does not know about this column
            // still has AI switched off.
            user.Property(appUser => appUser.AiEnabled).HasDefaultValue(false);
            user.Property(appUser => appUser.DisplayName).HasMaxLength(256);

            // Identity declares this index non-unique and enforces RequireUniqueEmail in
            // its validator instead. The linking branch in ExternalSignIn assumes one
            // account per address, and a validator cannot stop two concurrent first-time
            // sign-ins from both passing and both inserting. Postgres can.
            user.HasIndex(appUser => appUser.NormalizedEmail)
                .IsUnique()
                .HasDatabaseName("EmailIndex");
        });

        modelBuilder.ConfigureTransactions();
        modelBuilder.ConfigureImport();
        modelBuilder.ConfigureMarketData();

        // ADR-007, the part that matters: one loop, not one line per entity. A new
        // user-owned entity is isolated because it implements IUserOwned, not because
        // someone remembered to add a filter for it. prices and benchmarks are shared
        // market data, do not implement the interface, and are deliberately untouched
        // by this.
        foreach (var entityType in modelBuilder.Model.GetEntityTypes()
                     .Where(entityType => entityType.BaseType is null
                         && typeof(IUserOwned).IsAssignableFrom(entityType.ClrType)))
        {
            ApplyUserFilterMethod
                .MakeGenericMethod(entityType.ClrType)
                .Invoke(this, [modelBuilder]);
        }
    }

    /// <summary>
    /// Invoked reflectively, once per user-owned entity type, so the filter can be
    /// written as an ordinary typed lambda instead of a hand-built expression tree.
    /// </summary>
    private void ApplyUserFilter<TEntity>(ModelBuilder modelBuilder)
        where TEntity : class, IUserOwned =>
        modelBuilder.Entity<TEntity>().HasQueryFilter(entity => entity.UserId == CurrentUser.Id);
}
