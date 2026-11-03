using CivicLens.Core.Actions;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Actions.Persistence;

internal sealed class ActionRevisionIssueRowConfiguration : IEntityTypeConfiguration<ActionRevisionIssueRow>
{
    public void Configure(EntityTypeBuilder<ActionRevisionIssueRow> builder)
    {
        builder.ToTable("action_revision_issues", table =>
        {
            table.HasCheckConstraint("ck_action_revision_issues_position", "position >= 0");
            table.HasCheckConstraint("ck_action_revision_issues_issue", "length(btrim(issue_id)) > 0");
        });
        builder.HasKey(row => new { row.RecordId, row.RevisionNumber, row.IssueId });
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.IssueId).HasColumnName("issue_id").HasMaxLength(ActionDraftRevision.MaximumIssueIdLength);
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.IsTracked).HasColumnName("is_tracked");
        builder.HasIndex(row => new { row.IssueId, row.RecordId, row.RevisionNumber }).HasDatabaseName("ix_action_revision_issues_issue");
    }
}
