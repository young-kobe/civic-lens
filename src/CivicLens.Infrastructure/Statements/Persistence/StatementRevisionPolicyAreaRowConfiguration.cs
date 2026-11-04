using CivicLens.Core.Review;
using CivicLens.Core.Statements;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementRevisionPolicyAreaRowConfiguration : IEntityTypeConfiguration<StatementRevisionPolicyAreaRow>
{
    public void Configure(EntityTypeBuilder<StatementRevisionPolicyAreaRow> builder)
    {
        builder.ToTable("statement_revision_policy_areas", table =>
        {
            table.HasCheckConstraint("ck_statement_revision_policy_areas_position", $"position BETWEEN 0 AND {StatementDraftRevision.MaximumSelections - 1}");
            table.HasCheckConstraint("ck_statement_revision_policy_areas_policy_area", "length(btrim(policy_area_id)) > 0");
        });
        builder.HasKey(row => new { row.CandidateId, row.RevisionNumber, row.PolicyAreaId });
        builder.Property(row => row.CandidateId).HasColumnName("candidate_id").HasMaxLength(64);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.PolicyAreaId).HasColumnName("policy_area_id").HasMaxLength(ReviewTextRules.MaximumConfigurationIdLength);
        builder.Property(row => row.Position).HasColumnName("position");
    }
}
