using Finance.Api.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Infrastructure;

/// <summary>
/// The EF Core mapping for 003's three entities, kept out of
/// <see cref="AppDbContext.OnModelCreating"/> so that method stays about Identity and
/// the one loop that isolates every <see cref="Domain.IUserOwned"/> type.
/// </summary>
/// <remarks>
/// The query filters are deliberately absent from this file. They are applied by that
/// loop, to every entity implementing the interface, so a future entity's isolation
/// does not depend on someone remembering to configure it here (ADR-007).
/// </remarks>
internal static class TransactionsModel
{
    /// <summary>Long enough for a bank's own description field, short enough to index.</summary>
    private const int DescriptionLength = 300;

    private const int NameLength = 100;

    private const string CurrencyColumnType = "char(3)";

    public static ModelBuilder ConfigureTransactions(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Account>(account =>
        {
            account.Property(entity => entity.Name).HasMaxLength(NameLength).IsRequired();

            account.Property(entity => entity.Currency)
                .HasColumnType(CurrencyColumnType)
                .HasDefaultValue(Account.DefaultCurrency)
                .IsRequired();

            account.HasOne<Domain.Identity.AppUser>()
                .WithMany()
                .HasForeignKey(entity => entity.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // Two accounts called "Nubank" under one user is a mistake every time.
            // Scoped to the user, so it says nothing about anyone else's names.
            account.HasIndex(entity => new { entity.UserId, entity.Name }).IsUnique();

            // NOT NULL DEFAULT 0, so existing accounts start where they did —
            // their balance is the sum of their transactions.
            account.Property(entity => entity.OpeningBalance)
                .HasColumnType("numeric(18,2)")
                .HasDefaultValue(0m)
                .IsRequired();
        });

        modelBuilder.Entity<Category>(category =>
        {
            category.Property(entity => entity.Name).HasMaxLength(NameLength).IsRequired();

            category.HasOne<Domain.Identity.AppUser>()
                .WithMany()
                .HasForeignKey(entity => entity.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // RESTRICT, not CASCADE: deleting a parent must fail loudly rather than
            // take its children — and the transactions filed under them — with it.
            category.HasOne<Category>()
                .WithMany()
                .HasForeignKey(entity => entity.ParentId)
                .OnDelete(DeleteBehavior.Restrict);

            // NULLS NOT DISTINCT, because ParentId is null for every top-level
            // category and PostgreSQL otherwise treats those nulls as all different
            // from each other — which would leave the index constraining child names
            // and silently allowing a second top-level "Food" next to the seeded one.
            category.HasIndex(entity => new { entity.UserId, entity.ParentId, entity.Name })
                .IsUnique()
                .AreNullsDistinct(false);
        });

        modelBuilder.Entity<Transaction>(transaction =>
        {
            transaction.Property(entity => entity.Description)
                .HasMaxLength(DescriptionLength)
                .IsRequired();

            // Money is a value object with no identity, so it maps to two columns of
            // this table. OwnsOne would give it a primary key and change tracking it
            // has no use for.
            transaction.ComplexProperty(entity => entity.Money, money =>
            {
                money.Property(value => value.Amount)
                    .HasColumnName("Amount")
                    .HasColumnType("numeric(18,2)");

                money.Property(value => value.Currency)
                    .HasColumnName("Currency")
                    .HasColumnType(CurrencyColumnType);
            });

            transaction.HasOne<Domain.Identity.AppUser>()
                .WithMany()
                .HasForeignKey(entity => entity.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // RESTRICT on both: an account or a category with history behind it is not
            // something to delete by implication. The endpoints check first and answer
            // with a 409 that says which.
            transaction.HasOne<Account>()
                .WithMany()
                .HasForeignKey(entity => entity.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            transaction.HasOne<Category>()
                .WithMany()
                .HasForeignKey(entity => entity.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            // The access pattern of essentially every dashboard query: this user's
            // rows, newest first.
            transaction.HasIndex(entity => new { entity.UserId, entity.Date })
                .IsDescending(false, true);
        });

        return modelBuilder;
    }
}
