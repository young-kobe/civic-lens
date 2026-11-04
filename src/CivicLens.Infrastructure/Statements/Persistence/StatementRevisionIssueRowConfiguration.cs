using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementRevisionIssueRowConfiguration : IEntityTypeConfiguration<StatementRevisionIssueRow>
{
    public void Configure(EntityTypeBuilder<StatementRevisionIssueRow> builder)
    {
        builder.ToTable("statement_revision_issues", table =>
        {
            table.HasCheckConstraint("ck_statement_revision_issues_position", $"position BETWEEN 0 AND {StatementDraftRevision.MaximumSelections - 1}");
            table.HasCheckConstraint("ck_statement_revision_issues_issue", "length(btrim(issue_id)) > 0");
        });
        builder.HasKey(row => new { row.CandidateId, row.RevisionNumber, row.IssueId });
        builder.Property(row => row.CandidateId).HasColumnName("candidate_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.IssueId).HasColumnName("issue_id").HasMaxLength(ReviewTextRules.MaximumConfigurationIdLength);
        builder.Property(row => row.Position).HasColumnName("position");
    }
}
