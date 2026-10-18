using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Relationships.Persistence;

internal sealed class RelationshipAnalysisBudgetRowConfiguration : IEntityTypeConfiguration<RelationshipAnalysisBudgetRow>
{
    public void Configure(EntityTypeBuilder<RelationshipAnalysisBudgetRow> builder)
    {
        builder.ToTable("relationship_analysis_budget", table =>
            table.HasCheckConstraint("ck_relationship_analysis_budget_charge", "charged_tokens >= 0"));
        builder.HasKey(row => new { row.DayUtc, row.Task });
        builder.Property(row => row.DayUtc).HasColumnName("day_utc");
        builder.Property(row => row.Task).HasColumnName("task").HasMaxLength(32);
        builder.Property(row => row.ChargedTokens).HasColumnName("charged_tokens");
    }
}
