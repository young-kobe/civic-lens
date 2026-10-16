using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Briefs.Persistence;

internal sealed class BriefDraftRowConfiguration : IEntityTypeConfiguration<BriefDraftRow>
{
    public void Configure(EntityTypeBuilder<BriefDraftRow> builder)
    {
        builder.ToTable("brief_drafts", table =>
        {
            table.HasCheckConstraint("ck_brief_drafts_id", "record_id ~ '^[0-9a-f]{64}$'");
            table.HasCheckConstraint("ck_brief_drafts_week", "EXTRACT(ISODOW FROM week_start) = 1");
            table.HasCheckConstraint("ck_brief_drafts_revision", "current_revision_number >= 1");
            table.HasCheckConstraint("ck_brief_drafts_review_state_version", "review_state_version >= 0");
            table.HasCheckConstraint("ck_brief_drafts_review_status",
                "review_status IN ('AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn')");
        });
        builder.HasKey(row => row.RecordId);
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.WeekStart).HasColumnName("week_start");
        builder.Property(row => row.CurrentRevisionNumber).HasColumnName("current_revision_number");
        builder.Property(row => row.ReviewStateVersion).HasColumnName("review_state_version");
        builder.Property(row => row.ReviewStatus).HasColumnName("review_status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
        builder.Property(row => row.LastRevisedAtUtcTicks).HasColumnName("last_revised_at_utc_ticks");
        builder.HasIndex(row => row.WeekStart).IsUnique().HasDatabaseName("ux_brief_drafts_week");
    }
}
