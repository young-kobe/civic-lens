using CivicLens.Application.ExecutiveActions;
using CivicLens.Infrastructure.Analysis.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.ExecutiveActions.Persistence;

internal sealed class ExecutiveSummaryAnalysisRunRowConfiguration : IEntityTypeConfiguration<ExecutiveSummaryAnalysisRunRow>
{
    public void Configure(EntityTypeBuilder<ExecutiveSummaryAnalysisRunRow> builder)
    {
        AnalysisTableConfiguration.ConfigureRun(builder, "exec_summary_analysis_runs", nameof(ExecutiveSummaryAnalysisRunRow.RecordId),
            "record_id", table =>
            {
                table.HasCheckConstraint("ck_exec_summary_analysis_runs_ids",
                    "run_id ~ '^[0-9a-f]{32}$' AND input_hash ~ '^[0-9a-f]{64}$' AND (previous_run_id IS NULL OR previous_run_id ~ '^[0-9a-f]{32}$')");
                table.HasCheckConstraint("ck_exec_summary_analysis_runs_reason",
                    $"reason IS NULL OR reason IN ('{ExecutiveSummaryErrorCodes.Unchanged}','{ExecutiveSummaryErrorCodes.RecordRejected}','{ExecutiveSummaryErrorCodes.RevisionConflict}')");
                table.HasCheckConstraint("ck_exec_summary_analysis_runs_outcome",
                    "outcome IN ('Drafted','DraftDiscarded','CitationRejected','OutputRejected','Refused','OutputLimitReached','UnexpectedStop','RateLimited','ProviderUnavailable','AuthenticationFailed','ProviderRejected','ConnectionFailed','BillingFailed','ModelNotFound','ProcessingFailed','InvalidResponse','Interrupted','Bypassed')");
            });
        builder.Property(row => row.Reason).HasColumnName("reason").HasMaxLength(32);
        builder.HasOne<ExecutiveSummaryAnalysisRow>().WithMany()
            .HasForeignKey(row => new { row.RecordId, row.Task }).OnDelete(DeleteBehavior.Restrict);
    }
}
