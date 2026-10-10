using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Analysis.Persistence;

internal sealed class DocumentChangeAnalysisBudgetRowConfiguration : IEntityTypeConfiguration<DocumentChangeAnalysisBudgetRow>
{
    public void Configure(EntityTypeBuilder<DocumentChangeAnalysisBudgetRow> builder)
    {
        builder.ToTable("document_change_analysis_budget", table =>
            table.HasCheckConstraint("ck_document_change_analysis_budget_charge", "charged_tokens >= 0"));
        builder.HasKey(row => row.DayUtc);
        builder.Property(row => row.DayUtc).HasColumnName("day_utc");
        builder.Property(row => row.ChargedTokens).HasColumnName("charged_tokens");
    }
}
