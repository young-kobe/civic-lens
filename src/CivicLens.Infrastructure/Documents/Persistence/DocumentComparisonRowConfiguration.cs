using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Documents.Persistence;

internal sealed class DocumentComparisonRowConfiguration : IEntityTypeConfiguration<DocumentComparisonRow>
{
    public void Configure(EntityTypeBuilder<DocumentComparisonRow> builder)
    {
        builder.ToTable("document_comparisons", table =>
        {
            table.HasCheckConstraint("ck_document_comparisons_id", "comparison_id ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_document_comparisons_hunk_count", "hunk_count IS NULL OR (hunk_count >= 0 AND has_changes = (hunk_count > 0))");
        });
        builder.HasKey(row => row.ComparisonId);
        builder.Property(row => row.ComparisonId).HasColumnName("comparison_id").HasMaxLength(64);
        builder.Property(row => row.BeforeExtractionId).HasColumnName("before_extraction_id").HasMaxLength(64).IsRequired();
        builder.Property(row => row.AfterExtractionId).HasColumnName("after_extraction_id").HasMaxLength(64).IsRequired();
        builder.Property(row => row.AlgorithmVersion).HasColumnName("algorithm_version").HasMaxLength(128).IsRequired();
        builder.Property(row => row.SettingsVersion).HasColumnName("settings_version").HasMaxLength(128).IsRequired();
        builder.Property(row => row.ResultJson).HasColumnName("result_json").IsRequired();
        builder.Property(row => row.HasChanges).HasColumnName("has_changes").IsRequired();
        builder.Property(row => row.HunkCount).HasColumnName("hunk_count");
        builder.HasOne<DocumentExtractionRow>().WithMany().HasForeignKey(row => row.BeforeExtractionId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DocumentExtractionRow>().WithMany().HasForeignKey(row => row.AfterExtractionId).OnDelete(DeleteBehavior.Restrict);
    }
}
