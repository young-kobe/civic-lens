using CivicLens.Infrastructure.Review.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementDraftRowConfiguration : IEntityTypeConfiguration<StatementDraftRow>
{
    public void Configure(EntityTypeBuilder<StatementDraftRow> builder)
    {
        builder.ToTable("statement_drafts", table =>
        {
            table.HasCheckConstraint("ck_statement_drafts_revision", "current_revision_number >= 0");
            table.HasCheckConstraint("ck_statement_drafts_review_state_version", "review_state_version >= 0");
            table.HasCheckConstraint("ck_statement_drafts_review_status",
                "review_status IN ('NotDrafted','AwaitingReview','AwaitingReviewWithConcerns','Approved','ChangesRequested','ApprovalWithdrawn')");
            table.HasCheckConstraint("ck_statement_drafts_not_drafted", "(current_revision_number = 0) = (review_status = 'NotDrafted')");
        });
        builder.HasKey(row => row.CandidateId);
        builder.Property(row => row.CandidateId).HasColumnName("candidate_id").HasMaxLength(64);
        builder.Property(row => row.CurrentRevisionNumber).HasColumnName("current_revision_number");
        builder.Property(row => row.ReviewStateVersion).HasColumnName("review_state_version");
        builder.Property(row => row.ReviewStatus).HasColumnName("review_status").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
        ListedDraftTableConfiguration.ConfigureListing(builder, "statement_drafts");
        builder.HasIndex(nameof(StatementDraftRow.EventOn), nameof(StatementDraftRow.CandidateId)).IsDescending(true, true)
            .HasDatabaseName("ix_statement_drafts_listing");
        builder.HasOne<StatementCandidateRow>().WithMany().HasForeignKey(row => row.CandidateId).OnDelete(DeleteBehavior.Restrict);
    }
}
