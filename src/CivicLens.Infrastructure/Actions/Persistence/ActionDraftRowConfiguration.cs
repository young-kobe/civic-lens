using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Actions.Persistence;

internal sealed class ActionDraftRowConfiguration : IEntityTypeConfiguration<ActionDraftRow>
{
    public void Configure(EntityTypeBuilder<ActionDraftRow> builder)
    {
        builder.ToTable("action_drafts", table =>
        {
            table.HasCheckConstraint("ck_action_drafts_id", "record_id ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_action_drafts_bill", "congress >= 1 AND bill_number >= 1");
            table.HasCheckConstraint("ck_action_drafts_revision", "current_revision_number >= 1");
            table.HasCheckConstraint("ck_action_drafts_review_state_version", "review_state_version >= 0");
            table.HasCheckConstraint("ck_action_drafts_review_status",
                "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn')");
        });
        builder.HasKey(row => row.RecordId);
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.SourceId).HasColumnName("source_id").HasMaxLength(128).IsRequired();
        builder.Property(row => row.Congress).HasColumnName("congress");
        builder.Property(row => row.BillType).HasColumnName("bill_type").HasMaxLength(16).IsRequired();
        builder.Property(row => row.BillNumber).HasColumnName("bill_number");
        builder.Property(row => row.CurrentRevisionNumber).HasColumnName("current_revision_number");
        builder.Property(row => row.ReviewStateVersion).HasColumnName("review_state_version");
        builder.Property(row => row.ReviewStatus).HasColumnName("review_status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
        builder.Property(row => row.LastRevisedAtUtcTicks).HasColumnName("last_revised_at_utc_ticks");
        builder.HasIndex(row => new { row.SourceId, row.Congress, row.BillType, row.BillNumber }).IsUnique()
            .HasDatabaseName("ux_action_drafts_bill");
    }
}
