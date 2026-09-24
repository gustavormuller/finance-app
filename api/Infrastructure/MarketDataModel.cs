using Finance.Api.Domain.MarketData;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Infrastructure;

/// <summary>
/// The EF Core mapping for 006's shared market data. None of these types implements
/// <see cref="Domain.IUserOwned"/>, so the query-filter loop in
/// <see cref="AppDbContext.OnModelCreating"/> leaves them alone: every user reads the
/// same catalogue and the same series (ARCHITECTURE.md, Multi-tenancy).
/// </summary>
internal static class MarketDataModel
{
    /// <summary>Eight decimals: crypto quotes and daily CDI rates need them.</summary>
    private const string ValueColumnType = "numeric(18,8)";

    public static ModelBuilder ConfigureMarketData(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<MarketAsset>(asset =>
        {
            asset.Property(entity => entity.Ticker).HasMaxLength(20).IsRequired();
            asset.Property(entity => entity.Name).HasMaxLength(200).IsRequired();
            asset.Property(entity => entity.Currency).HasColumnType("char(3)").IsRequired();
            asset.Property(entity => entity.ProviderSymbol).HasMaxLength(50).IsRequired();

            // A provider's symbol names one series; the same ticker at two providers
            // is two rows.
            asset.HasIndex(entity => new { entity.Provider, entity.ProviderSymbol }).IsUnique();
        });

        modelBuilder.Entity<Price>(price =>
        {
            price.HasKey(entity => new { entity.MarketAssetId, entity.Date });
            price.Property(entity => entity.Close).HasColumnType(ValueColumnType);

            // RESTRICT: five years of closes are not dropped by an accidental
            // catalogue delete. Nothing deletes an asset yet; one that must go has its
            // prices deleted first, on purpose.
            price.HasOne<MarketAsset>()
                .WithMany()
                .HasForeignKey(entity => entity.MarketAssetId)
                .OnDelete(DeleteBehavior.Restrict);
        });

        modelBuilder.Entity<Benchmark>(benchmark =>
        {
            benchmark.HasKey(entity => new { entity.Code, entity.Date });
            benchmark.Property(entity => entity.Code).HasMaxLength(20);
            benchmark.Property(entity => entity.Value).HasColumnType(ValueColumnType);
        });

        modelBuilder.Entity<SyncRun>(run =>
        {
            run.Property(entity => entity.Summary).HasColumnType("jsonb").IsRequired();

            // The last-20 list and the startup "older than 26 hours" check, both
            // newest first.
            run.HasIndex(entity => entity.StartedAt).IsDescending();
        });

        return modelBuilder;
    }
}
