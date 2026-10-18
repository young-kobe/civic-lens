using CivicLens.Application.Relationships;
using CivicLens.Infrastructure.Analysis.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Relationships.Persistence;

internal sealed class RelationshipAnalysisRunRowConfiguration : IEntityTypeConfiguration<RelationshipAnalysisRunRow>
{
    public void Configure(EntityTypeBuilder<RelationshipAnalysisRunRow> builder)
    {
        AnalysisTableConfiguration.ConfigureRun(builder, "relationship_analysis_runs", nameof(RelationshipAnalysisRunRow.PairId),
            "pair_id", table =>
            {
                table.HasCheckConstraint("ck_relationship_analysis_runs_ids",
                    "run_id ~ '^[0-9a-f]{32}$' AND input_hash ~ '^[0-9a-f]{64}$' AND (previous_run_id IS NULL OR previous_run_id ~ '^[0-9a-f]{32}$')");
                table.HasCheckConstraint("ck_relationship_analysis_runs_reason",
                    $"reason IS NULL OR reason IN ('{RelationshipDraftErrorCodes.Unchanged}','{RelationshipDraftErrorCodes.NoRelationship}','{RelationshipDraftErrorCodes.NoLongerMatches}')");
                table.HasCheckConstraint("ck_relationship_analysis_runs_outcome",
                    "outcome IN ('Drafted','DraftDiscarded','CitationRejected','OutputRejected','Refused','OutputLimitReached','UnexpectedStop','RateLimited','ProviderUnavailable','AuthenticationFailed','ProviderRejected','ConnectionFailed','BillingFailed','ModelNotFound','ProcessingFailed','InvalidResponse','Interrupted','Bypassed')");
            });
        builder.Property(row => row.Reason).HasColumnName("reason").HasMaxLength(32);
        builder.HasOne<RelationshipAnalysisRow>().WithMany()
            .HasForeignKey(row => new { row.PairId, row.Task }).OnDelete(DeleteBehavior.Restrict);
    }
}
