using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Review.Persistence;

internal sealed class DocumentChangeDraftRowConfiguration : IEntityTypeConfiguration<DocumentChangeDraftRow>
{
    public void Configure(EntityTypeBuilder<DocumentChangeDraftRow> builder)
    {
        builder.ToTable("document_change_drafts", table =>
        {
            table.HasCheckConstraint("ck_document_change_drafts_id", "draft_id ~ '^[0-9a-f]{32}$'");
            table.HasCheckConstraint("ck_document_change_drafts_versions", "current_revision_number >= 1 AND review_state_version >= 0");
        });
        builder.HasKey(row => row.DraftId);
        builder.Property(row => row.DraftId).HasColumnName("draft_id").HasMaxLength(32);
        builder.Property(row => row.ComparisonId).HasColumnName("comparison_id").HasMaxLength(64).IsRequired();
        builder.Property(row => row.CurrentRevisionNumber).HasColumnName("current_revision_number").IsRequired();
        builder.Property(row => row.ReviewStateVersion).HasColumnName("review_state_version").IsRequired();
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks").IsRequired();
        builder.HasOne<Documents.Persistence.DocumentComparisonRow>().WithMany()
            .HasForeignKey(row => row.ComparisonId).OnDelete(DeleteBehavior.Restrict);
    }
}
