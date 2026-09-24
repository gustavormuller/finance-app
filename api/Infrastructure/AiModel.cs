using Finance.Api.Domain.Ai;
using Finance.Api.Domain.Identity;
using Microsoft.EntityFrameworkCore;

namespace Finance.Api.Infrastructure;

/// <summary>
/// The EF Core mapping for 009's AI module. Both types implement
/// <see cref="Domain.IUserOwned"/>, so the query filter comes from the loop in
/// <see cref="AppDbContext.OnModelCreating"/>, not from this file (ADR-007).
/// </summary>
internal static class AiModel
{
    /// <summary><c>YYYY-MM</c>, as the dashboard's <c>?month=</c> spells it.</summary>
    private const string MonthColumnType = "char(7)";

    /// <summary>
    /// Money, four places: a categorisation call costs fractions of a centavo, and the
    /// budget is the sum of many of them (spec data model).
    /// </summary>
    private const string CostColumnType = "numeric(10,4)";

    public static ModelBuilder ConfigureAi(this ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<AiUsage>(usage =>
        {
            usage.ToTable("AiUsage");

            usage.Property(entity => entity.Month).HasColumnType(MonthColumnType);
            usage.Property(entity => entity.Provider).HasMaxLength(20);
            usage.Property(entity => entity.Model).HasMaxLength(100);
            usage.Property(entity => entity.CostBrl).HasColumnType(CostColumnType);

            usage.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(entity => entity.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // The budget guard sums one user's month before every call.
            usage.HasIndex(entity => new { entity.UserId, entity.Month });
        });

        modelBuilder.Entity<AiAnalysis>(analysis =>
        {
            analysis.Property(entity => entity.Month).HasColumnType(MonthColumnType);
            analysis.Property(entity => entity.Content).HasColumnType("text");
            analysis.Property(entity => entity.Error).HasMaxLength(500);
            analysis.Property(entity => entity.PromptVersion).HasMaxLength(20);

            analysis.HasOne<AppUser>()
                .WithMany()
                .HasForeignKey(entity => entity.UserId)
                .OnDelete(DeleteBehavior.Cascade);

            // One analysis per user per month: regenerating replaces (spec test 23).
            analysis.HasIndex(entity => new { entity.UserId, entity.Month }).IsUnique();
        });

        return modelBuilder;
    }
}
