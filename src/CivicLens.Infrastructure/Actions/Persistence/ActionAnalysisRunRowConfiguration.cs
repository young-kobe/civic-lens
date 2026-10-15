using CivicLens.Infrastructure.Analysis.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Actions.Persistence;

internal sealed class ActionAnalysisRunRowConfiguration : IEntityTypeConfiguration<ActionAnalysisRunRow>
{
    public void Configure(EntityTypeBuilder<ActionAnalysisRunRow> builder)
    {
        AnalysisTableConfiguration.ConfigureRun(builder, "action_analysis_runs", nameof(ActionAnalysisRunRow.CandidateId),
            "candidate_id", table =>
            {
                table.HasCheckConstraint("ck_action_analysis_runs_ids",
                    "run_id ~ '^[0-9a-f]{32}$' AND input_hash ~ '^[0-9a-f]{64}$' AND (previous_run_id IS NULL OR previous_run_id ~ '^[0-9a-f]{32}$')");
                table.HasCheckConstraint("ck_action_analysis_runs_reason",
                    "reason IS NULL OR reason IN ('unchanged','noTrackedOfficial','summaryOmitted','sponsorshipsChanged')");
                table.HasCheckConstraint("ck_action_analysis_runs_outcome",
                    "outcome IN ('Drafted','DraftDiscarded','CitationRejected','OutputRejected','Refused','OutputLimitReached','UnexpectedStop','RateLimited','ProviderUnavailable','AuthenticationFailed','ProviderRejected','ConnectionFailed','BillingFailed','ModelNotFound','ProcessingFailed','InvalidResponse','Interrupted','Bypassed')");
            });
        builder.Property(row => row.Reason).HasColumnName("reason").HasMaxLength(32);
        builder.HasOne<ActionAnalysisRow>().WithMany()
            .HasForeignKey(row => new { row.CandidateId, row.Task }).OnDelete(DeleteBehavior.Restrict);
    }
}
