using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Review.Persistence;

internal sealed class ReviewDecisionRowConfiguration : IEntityTypeConfiguration<ReviewDecisionRow>
{
    public void Configure(EntityTypeBuilder<ReviewDecisionRow> builder)
    {
        builder.ToTable("document_change_review_decisions", table =>
        {
            table.HasCheckConstraint("ck_document_change_review_decisions_id", "decision_id ~ '^[0-9a-f]{32}$'");
            table.HasCheckConstraint("ck_document_change_review_decisions_versions", "revision_number >= 1 AND review_state_version >= 1");
        });
        builder.HasKey(row => row.DecisionId);
        builder.Property(row => row.DecisionId).HasColumnName("decision_id").HasMaxLength(32);
        builder.Property(row => row.DraftId).HasColumnName("draft_id").HasMaxLength(32).IsRequired();
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number").IsRequired();
        builder.Property(row => row.ReviewStateVersion).HasColumnName("review_state_version").IsRequired();
        builder.Property(row => row.DecisionJson).HasColumnName("decision_json").IsRequired();
        builder.HasOne<DocumentChangeRevisionRow>().WithMany()
            .HasForeignKey(row => new { row.DraftId, row.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => new { row.DraftId, row.ReviewStateVersion }).IsUnique();
    }
}
