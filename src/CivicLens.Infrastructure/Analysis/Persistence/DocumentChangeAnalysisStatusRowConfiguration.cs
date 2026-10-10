using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Analysis.Persistence;

internal sealed class DocumentChangeAnalysisStatusRowConfiguration : IEntityTypeConfiguration<DocumentChangeAnalysisStatusRow>
{
    public void Configure(EntityTypeBuilder<DocumentChangeAnalysisStatusRow> builder)
    {
        builder.ToTable("document_change_analysis_status", table =>
        {
            table.HasCheckConstraint("ck_document_change_analysis_status_limit", "daily_token_limit IS NULL OR daily_token_limit > 0");
            table.HasCheckConstraint("ck_document_change_analysis_status_pause",
                "paused_until_utc_ticks IS NULL OR paused_reason IS NOT NULL");
        });
        builder.HasKey(row => row.Task);
        builder.Property(row => row.Task).HasColumnName("task").HasMaxLength(32);
        builder.Property(row => row.DailyTokenLimit).HasColumnName("daily_token_limit");
        builder.Property(row => row.RecordedAtUtcTicks).HasColumnName("recorded_at_utc_ticks");
        builder.Property(row => row.PausedReason).HasColumnName("paused_reason").HasMaxLength(64);
        builder.Property(row => row.PausedUntilUtcTicks).HasColumnName("paused_until_utc_ticks");
    }
}
