using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Votes.Persistence;

internal sealed class VoteRevisionIssueRowConfiguration : IEntityTypeConfiguration<VoteRevisionIssueRow>
{
    public void Configure(EntityTypeBuilder<VoteRevisionIssueRow> builder)
    {
        builder.ToTable("vote_revision_issues", table =>
        {
            table.HasCheckConstraint("ck_vote_revision_issues_position", "position >= 0");
            table.HasCheckConstraint("ck_vote_revision_issues_issue", "length(btrim(issue_id)) > 0");
        });
        builder.HasKey(row => new { row.RecordId, row.RevisionNumber, row.IssueId });
        builder.Property(row => row.RecordId).HasColumnName("record_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.IssueId).HasColumnName("issue_id").HasMaxLength(128);
        builder.Property(row => row.Position).HasColumnName("position");
        builder.Property(row => row.IsTracked).HasColumnName("is_tracked");
    }
}
