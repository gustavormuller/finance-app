using Finance.Api.Domain.Import;
using Finance.Api.Domain.Transactions;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Infrastructure;

/// <summary>
/// The EF Core mapping for 004: the three import entities and the three columns
/// added to <see cref="Transaction"/>. Kept out of
/// <see cref="AppDbContext.OnModelCreating"/> for the same reason
/// <see cref="TransactionsModel"/> is; the query filters are applied by that
/// method's loop to every <see cref="Domain.IUserOwned"/> type, never here.
/// </summary>
internal static class ImportModel
{
    private const int DescriptionLength = 300;

    private const int ExternalIdLength = 100;

    private const int ColumnReferenceLength = 100;

    /// <summary>Windows' MAX_PATH, which is as long as an uploaded name gets.</summary>
    private const int FileNameLength = 260;

    private const string CurrencyColumnType = "char(3)";

    public static ModelBuilder ConfigureImport(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Transaction>(transaction =>
        {
            transaction.Property(entity => entity.ExternalId).HasMaxLength(ExternalIdLength);
            transaction.Property(entity => entity.NormalizedDescription).HasMaxLength(DescriptionLength);

            // RESTRICT: undo deletes the transactions first, then the batch,
            // explicitly and in one database transaction. A cascade would let an
            // accidental batch delete silently take the transactions with it.
            transaction.HasOne<ImportBatch>()
                .WithMany()
                .HasForeignKey(entity => entity.ImportBatchId)
                .OnDelete(DeleteBehavior.Restrict);

            // Partial: manual rows have no ExternalId and are unaffected.
            transaction.HasIndex(entity => new { entity.UserId, entity.AccountId, entity.ExternalId })
                .IsUnique()
                .HasFilter("\"ExternalId\" IS NOT NULL");

            // The heuristic dedupe and the history lookup, both by this key.
            transaction.HasIndex(entity => new { entity.UserId, entity.Date, entity.NormalizedDescription });
        });

        modelBuilder.Entity<ImportBatch>(batch =>
        {
            batch.Property(entity => entity.FileName).HasMaxLength(FileNameLength).IsRequired();

            batch.HasOne<Domain.Identity.AppUser>()
                .WithMany()
                .HasForeignKey(entity => entity.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            batch.HasOne<Account>()
                .WithMany()
                .HasForeignKey(entity => entity.AccountId)
                .OnDelete(DeleteBehavior.Restrict);

            // One open batch per user, enforced by the database rather than by a
            // pre-check that races with a concurrent upload.
            batch.HasIndex(entity => entity.UserId)
                .IsUnique()
                .HasFilter($"\"Status\" = {(int)ImportBatchStatus.Staged}")
                .HasDatabaseName("IX_ImportBatches_UserId_Staged");

            // The history screen: this user's batches, newest first.
            batch.HasIndex(entity => new { entity.UserId, entity.CreatedAt })
                .IsDescending(false, true);
        });

        modelBuilder.Entity<StagedTransaction>(staged =>
        {
            staged.Property(entity => entity.Amount).HasColumnType("numeric(18,2)");
            staged.Property(entity => entity.Currency).HasColumnType(CurrencyColumnType);

            // text, not varchar(300): the full description is kept here and only
            // truncated on commit (spec decision 10).
            staged.Property(entity => entity.RawDescription).HasColumnType("text").IsRequired();
            staged.Property(entity => entity.NormalizedDescription).HasMaxLength(DescriptionLength);
            staged.Property(entity => entity.ExternalId).HasMaxLength(ExternalIdLength);
            staged.Property(entity => entity.Issues).HasColumnType("text");

            staged.HasOne<Domain.Identity.AppUser>()
                .WithMany()
                .HasForeignKey(entity => entity.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // CASCADE: staging rows have no life of their own.
            staged.HasOne<ImportBatch>()
                .WithMany()
                .HasForeignKey(entity => entity.ImportBatchId)
                .OnDelete(DeleteBehavior.Cascade);

            staged.HasOne<Category>()
                .WithMany()
                .HasForeignKey(entity => entity.CategoryId)
                .OnDelete(DeleteBehavior.Restrict);

            // The preview reads one batch in file order.
            staged.HasIndex(entity => new { entity.ImportBatchId, entity.RowNumber });
        });

        modelBuilder.Entity<CsvTemplate>(template =>
        {
            template.Property(entity => entity.Name).HasMaxLength(100).IsRequired();
            template.Property(entity => entity.Delimiter).HasColumnType("char(1)");
            template.Property(entity => entity.Culture).HasMaxLength(10).IsRequired();
            template.Property(entity => entity.DateFormat).HasMaxLength(20).IsRequired();
            template.Property(entity => entity.DateColumn).HasMaxLength(ColumnReferenceLength).IsRequired();
            template.Property(entity => entity.AmountColumn).HasMaxLength(ColumnReferenceLength);
            template.Property(entity => entity.DebitColumn).HasMaxLength(ColumnReferenceLength);
            template.Property(entity => entity.CreditColumn).HasMaxLength(ColumnReferenceLength);
            template.Property(entity => entity.DescriptionColumns).HasMaxLength(DescriptionLength).IsRequired();

            template.HasOne<Domain.Identity.AppUser>()
                .WithMany()
                .HasForeignKey(entity => entity.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            template.HasIndex(entity => new { entity.UserId, entity.Name }).IsUnique();
        });

        return modelBuilder;
    }
}
