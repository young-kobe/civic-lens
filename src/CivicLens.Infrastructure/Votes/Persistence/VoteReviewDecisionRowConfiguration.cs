using CivicLens.Infrastructure.Review.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Votes.Persistence;

internal sealed class VoteReviewDecisionRowConfiguration : IEntityTypeConfiguration<VoteReviewDecisionRow>
{
    public void Configure(EntityTypeBuilder<VoteReviewDecisionRow> builder)
    {
        builder.ToTable("vote_review_decisions", table =>
        {
            table.HasCheckConstraint("ck_vote_review_decisions_id", "decision_id ~ '^[0-9a-f]{32}$'");
            table.HasCheckConstraint("ck_vote_review_decisions_versions", "revision_number >= 1 AND review_state_version >= 1");
        });
        builder.HasKey(row => row.DecisionId);
        builder.Property(row => row.DecisionId).HasColumnName("decision_id").HasMaxLength(32);
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64).IsRequired();
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number").IsRequired();
        builder.Property(row => row.ReviewStateVersion).HasColumnName("review_state_version").IsRequired();
        builder.Property(row => row.Kind).HasColumnName("kind").HasConversion<string>().IsRequired();
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks").IsRequired();
        ReviewDecisionTableConfiguration.ConfigureDecisionColumns(builder, "vote_review_decisions", nameof(VoteReviewDecisionRow.RecordId));
        builder.HasOne<VoteRevisionRow>().WithMany()
            .HasForeignKey(row => new { row.RecordId, row.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => new { row.RecordId, row.ReviewStateVersion }).IsUnique();
    }
}
