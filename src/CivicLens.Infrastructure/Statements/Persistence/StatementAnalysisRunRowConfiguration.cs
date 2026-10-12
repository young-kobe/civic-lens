using CivicLens.Infrastructure.Analysis.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementAnalysisRunRowConfiguration : IEntityTypeConfiguration<StatementAnalysisRunRow>
{
    public void Configure(EntityTypeBuilder<StatementAnalysisRunRow> builder)
    {
        AnalysisTableConfiguration.ConfigureRun(builder, "statement_analysis_runs", nameof(StatementAnalysisRunRow.CandidateId),
            "candidate_id", table =>
            {
                table.HasCheckConstraint("ck_statement_analysis_runs_ids",
                    "run_id ~ '^[0-9a-f]{32}$' AND input_hash ~ '^[0-9a-f]{64}$' AND (issue_revision_id IS NULL OR issue_revision_id ~ '^[0-9a-f]{64}$') AND (previous_run_id IS NULL OR previous_run_id ~ '^[0-9a-f]{32}$')");
                table.HasCheckConstraint("ck_statement_analysis_runs_revision", "issue_revision_id IS NOT NULL OR outcome = 'Interrupted'");
                table.HasCheckConstraint("ck_statement_analysis_runs_reason",
                    "reason IS NULL OR reason IN ('passed','no-tracked-issue','over-byte-cap','gate-failed-open')");
                table.HasCheckConstraint("ck_statement_analysis_runs_outcome",
                    "outcome IN ('Drafted','DraftDiscarded','CitationRejected','OutputRejected','Refused','OutputLimitReached','UnexpectedStop','RateLimited','ProviderUnavailable','AuthenticationFailed','ProviderRejected','ConnectionFailed','BillingFailed','ModelNotFound','ProcessingFailed','InvalidResponse','Interrupted','Classified','Bypassed')");
            });
        builder.Property(row => row.IssueRevisionId).HasColumnName("issue_revision_id").HasMaxLength(64);
        builder.Property(row => row.Reason).HasColumnName("reason").HasMaxLength(32);
        builder.Property(row => row.QuotesJson).HasColumnName("quotes_json");
        builder.HasOne<StatementAnalysisRow>().WithMany()
            .HasForeignKey(row => new { row.CandidateId, row.Task }).OnDelete(DeleteBehavior.Restrict);
    }
}
