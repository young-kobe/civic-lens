using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Documents.Persistence;

internal sealed class DocumentComparisonHunkRowConfiguration : IEntityTypeConfiguration<DocumentComparisonHunkRow>
{
    public void Configure(EntityTypeBuilder<DocumentComparisonHunkRow> builder)
    {
        builder.ToTable("document_comparison_hunks", table =>
        {
            table.HasCheckConstraint("ck_document_comparison_hunks_position", "position >= 0");
            table.HasCheckConstraint("ck_document_comparison_hunks_word_edit_count", "word_edit_count >= 0");
            table.HasCheckConstraint("ck_document_comparison_hunks_spans", "before_start >= 0 AND before_length >= 0 AND after_start >= 0 AND after_length >= 0 AND before_context_start >= 0 AND before_context_length >= 0 AND after_context_start >= 0 AND after_context_length >= 0");
        });
        builder.HasKey(row => new { row.ComparisonId, row.Position });
        builder.Property(row => row.ComparisonId).HasColumnName("comparison_id").HasMaxLength(64);
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.BeforeStart).HasColumnName("before_start");
        builder.Property(row => row.BeforeLength).HasColumnName("before_length");
        builder.Property(row => row.AfterStart).HasColumnName("after_start");
        builder.Property(row => row.AfterLength).HasColumnName("after_length");
        builder.Property(row => row.BeforeContextStart).HasColumnName("before_context_start");
        builder.Property(row => row.BeforeContextLength).HasColumnName("before_context_length");
        builder.Property(row => row.AfterContextStart).HasColumnName("after_context_start");
        builder.Property(row => row.AfterContextLength).HasColumnName("after_context_length");
        builder.Property(row => row.WordEditCount).HasColumnName("word_edit_count");
        builder.HasOne<DocumentComparisonRow>().WithMany(row => row.Hunks)
            .HasForeignKey(row => row.ComparisonId).OnDelete(DeleteBehavior.Cascade);
        builder.HasMany(row => row.WordEdits).WithOne().HasForeignKey(row => new { row.ComparisonId, row.HunkPosition })
            .HasPrincipalKey(row => new { row.ComparisonId, row.Position }).OnDelete(DeleteBehavior.Cascade);
    }
}
