using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Documents.Persistence;

internal sealed class DocumentComparisonWordEditRowConfiguration : IEntityTypeConfiguration<DocumentComparisonWordEditRow>
{
    public void Configure(EntityTypeBuilder<DocumentComparisonWordEditRow> builder)
    {
        builder.ToTable("document_comparison_word_edits", table =>
        {
            table.HasCheckConstraint("ck_document_comparison_word_edits_position", "hunk_position >= 0 AND position >= 0");
            table.HasCheckConstraint("ck_document_comparison_word_edits_spans", "before_start >= 0 AND before_length >= 0 AND after_start >= 0 AND after_length >= 0");
        });
        builder.HasKey(row => new { row.ComparisonId, row.HunkPosition, row.Position });
        builder.Property(row => row.ComparisonId).HasColumnName("comparison_id").HasMaxLength(64);
        builder.Property(row => row.HunkPosition).HasColumnName("hunk_position");
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.BeforeStart).HasColumnName("before_start");
        builder.Property(row => row.BeforeLength).HasColumnName("before_length");
        builder.Property(row => row.AfterStart).HasColumnName("after_start");
        builder.Property(row => row.AfterLength).HasColumnName("after_length");
    }
}
