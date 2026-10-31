using CivicLens.Infrastructure.Review.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Relationships.Persistence;

internal sealed class RelationshipDraftRowConfiguration : IEntityTypeConfiguration<RelationshipDraftRow>
{
    public void Configure(EntityTypeBuilder<RelationshipDraftRow> builder)
    {
        builder.ToTable("relationship_drafts", table =>
        {
            table.HasCheckConstraint("ck_relationship_drafts_id", "record_id ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_relationship_drafts_revision", "current_revision_number >= 1");
            table.HasCheckConstraint("ck_relationship_drafts_review_state_version", "review_state_version >= 0");
            table.HasCheckConstraint("ck_relationship_drafts_review_status",
                "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn')");
        });
        builder.HasKey(row => row.RecordId);
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.CurrentRevisionNumber).HasColumnName("current_revision_number");
        builder.Property(row => row.ReviewStateVersion).HasColumnName("review_state_version");
        builder.Property(row => row.ReviewStatus).HasColumnName("review_status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(row => row.NoLongerMatches).HasColumnName("no_longer_matches");
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
        ReviewDraftTableConfiguration.ConfigureLastRevised(builder, "relationship_drafts", nameof(RelationshipDraftRow.RecordId));
    }
}
