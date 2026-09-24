using Finance.Api.Domain.Identity;
using Finance.Api.Domain.Investments;
using Finance.Api.Domain.MarketData;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Infrastructure;

/// <summary>
/// The EF Core mapping for 007's investments. All three types implement
/// <see cref="Domain.IUserOwned"/>, so the query filter comes from the loop in
/// <see cref="AppDbContext.OnModelCreating"/>, not from this file (ADR-007).
/// </summary>
internal static class InvestmentsModel
{
    /// <summary>Quantities, unit prices, average cost and FX rates: measurements, eight decimals.</summary>
    private const string MeasureColumnType = "numeric(18,8)";

    /// <summary>Cash and BRL totals: two decimals, like every ledger amount.</summary>
    private const string CashColumnType = "numeric(18,2)";

    public static ModelBuilder ConfigureInvestments(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<Asset>(asset =>
        {
            asset.Property(entity => entity.Nickname).HasMaxLength(100);

            asset.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(entity => entity.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // RESTRICT: a catalogue row someone holds keeps its price series.
            asset.HasOne<MarketAsset>()
                .WithMany()
                .HasForeignKey(entity => entity.MarketAssetId)
                .OnDelete(DeleteBehavior.Restrict);

            // One position per user per instrument (spec test 23).
            asset.HasIndex(entity => new { entity.UserId, entity.MarketAssetId }).IsUnique();
        });

        modelBuilder.Entity<Movement>(movement =>
        {
            movement.Property(entity => entity.Quantity).HasColumnType(MeasureColumnType);
            movement.Property(entity => entity.UnitPrice).HasColumnType(MeasureColumnType);
            movement.Property(entity => entity.Amount).HasColumnType(CashColumnType);
            movement.Property(entity => entity.Fees).HasColumnType(CashColumnType);
            movement.Property(entity => entity.Currency).HasColumnType("char(3)").IsRequired();
            movement.Property(entity => entity.Notes).HasMaxLength(300);

            movement.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(entity => entity.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // RESTRICT: an asset with history is not deleted by implication; the
            // endpoint answers 409 first (spec test 24).
            movement.HasOne<Asset>()
                .WithMany()
                .HasForeignKey(entity => entity.AssetId)
                .OnDelete(DeleteBehavior.Restrict);

            // Every position calculation: one user's asset, in date order.
            movement.HasIndex(entity => new { entity.UserId, entity.AssetId, entity.Date });
        });

        modelBuilder.Entity<PortfolioDaily>(daily =>
        {
            daily.ToTable("PortfolioDaily");
            daily.HasKey(entity => new { entity.UserId, entity.AssetId, entity.Date });

            daily.Property(entity => entity.Quantity).HasColumnType(MeasureColumnType);
            daily.Property(entity => entity.AverageCost).HasColumnType(MeasureColumnType);
            daily.Property(entity => entity.Price).HasColumnType(MeasureColumnType);
            daily.Property(entity => entity.FxRate).HasColumnType(MeasureColumnType);
            daily.Property(entity => entity.ValueBrl).HasColumnType(CashColumnType);
            daily.Property(entity => entity.CostBasisBrl).HasColumnType(CashColumnType);

            daily.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(entity => entity.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // CASCADE: derived rows never hold their asset in place (ADR-011).
            daily.HasOne<Asset>()
                .WithMany()
                .HasForeignKey(entity => entity.AssetId)
                .OnDelete(DeleteBehavior.Cascade);
        });

        return modelBuilder;
    }
}
