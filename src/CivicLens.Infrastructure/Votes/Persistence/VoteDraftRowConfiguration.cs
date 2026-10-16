using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Votes.Persistence;

internal sealed class VoteDraftRowConfiguration : IEntityTypeConfiguration<VoteDraftRow>
{
    public void Configure(EntityTypeBuilder<VoteDraftRow> builder)
    {
        builder.ToTable("vote_drafts", table =>
        {
            table.HasCheckConstraint("ck_vote_drafts_id", "record_id ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_vote_drafts_roll", "congress >= 1 AND session_number IN (1, 2) AND roll_call_number >= 1");
            table.HasCheckConstraint("ck_vote_drafts_revision", "current_revision_number >= 1");
            table.HasCheckConstraint("ck_vote_drafts_review_state_version", "review_state_version >= 0");
            table.HasCheckConstraint("ck_vote_drafts_review_status",
                "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn')");
        });
        builder.HasKey(row => row.RecordId);
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.SourceId).HasColumnName("source_id").HasMaxLength(128).IsRequired();
        builder.Property(row => row.Congress).HasColumnName("congress");
        builder.Property(row => row.SessionNumber).HasColumnName("session_number");
        builder.Property(row => row.RollCallNumber).HasColumnName("roll_call_number");
        builder.Property(row => row.CurrentRevisionNumber).HasColumnName("current_revision_number");
        builder.Property(row => row.ReviewStateVersion).HasColumnName("review_state_version");
        builder.Property(row => row.ReviewStatus).HasColumnName("review_status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
        builder.Property(row => row.LastRevisedAtUtcTicks).HasColumnName("last_revised_at_utc_ticks");
        builder.HasIndex(row => new { row.SourceId, row.Congress, row.SessionNumber, row.RollCallNumber }).IsUnique()
            .HasDatabaseName("ux_vote_drafts_roll");
    }
}
