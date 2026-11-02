using CivicLens.Infrastructure.Analysis.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Briefs.Persistence;

internal sealed class BriefAnalysisRowConfiguration : IEntityTypeConfiguration<BriefAnalysisRow>
{
    public void Configure(EntityTypeBuilder<BriefAnalysisRow> builder)
    {
        AnalysisTableConfiguration.ConfigureQueue<BriefAnalysisRow, BriefAnalysisRunRow>(builder, "brief_analysis", nameof(BriefAnalysisRow.RecordId), "record_id");
        builder.ToTable("brief_analysis", table => table.HasCheckConstraint("ck_brief_analysis_week", "EXTRACT(ISODOW FROM week_start) = 1"));
        builder.Property(row => row.WeekStart).HasColumnName("week_start");
        builder.HasIndex(row => row.WeekStart).IsUnique().HasDatabaseName("ux_brief_analysis_week");
    }
}
