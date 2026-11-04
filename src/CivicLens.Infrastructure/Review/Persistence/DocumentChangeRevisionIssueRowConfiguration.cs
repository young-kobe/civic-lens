using CivicLens.Core.Review;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Review.Persistence;

internal sealed class DocumentChangeRevisionIssueRowConfiguration : IEntityTypeConfiguration<DocumentChangeRevisionIssueRow>
{
    public void Configure(EntityTypeBuilder<DocumentChangeRevisionIssueRow> builder)
    {
        builder.ToTable("document_change_revision_issues", table =>
        {
            table.HasCheckConstraint("ck_document_change_revision_issues_position", $"position BETWEEN 0 AND {DocumentChangeDraftRevision.MaximumSelections - 1}");
            table.HasCheckConstraint("ck_document_change_revision_issues_issue", "length(btrim(issue_id)) > 0");
        });
        builder.HasKey(row => new { row.DraftId, row.RevisionNumber, row.IssueId });
        builder.Property(row => row.DraftId).HasColumnName("draft_id").HasMaxLength(32);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.IssueId).HasColumnName("issue_id").HasMaxLength(ReviewTextRules.MaximumConfigurationIdLength);
        builder.Property(row => row.Position).HasColumnName("position");
    }
}
