using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementAnalysisRunRowConfiguration : IEntityTypeConfiguration<StatementAnalysisRunRow>
{
    public void Configure(EntityTypeBuilder<StatementAnalysisRunRow> builder)
    {
        builder.ToTable("statement_analysis_runs", table =>
        {
            table.HasCheckConstraint("ck_statement_analysis_runs_ids",
                "run_id ~ '^[0-9a-f]{32}$' AND input_hash ~ '^[0-9a-f]{64}$' AND (issue_revision_id IS NULL OR issue_revision_id ~ '^[0-9a-f]{64}$') AND (previous_run_id IS NULL OR previous_run_id ~ '^[0-9a-f]{32}$')");
            table.HasCheckConstraint("ck_statement_analysis_runs_tokens",
                "input_tokens >= 0 AND output_tokens >= 0 AND cache_read_tokens >= 0 AND cache_write_tokens >= 0 AND " +
                "charged_tokens >= input_tokens + output_tokens + cache_read_tokens + cache_write_tokens");
            table.HasCheckConstraint("ck_statement_analysis_runs_times", "finished_at_utc_ticks >= started_at_utc_ticks");
            table.HasCheckConstraint("ck_statement_analysis_runs_revision", "issue_revision_id IS NOT NULL OR outcome = 'Interrupted'");
            table.HasCheckConstraint("ck_statement_analysis_runs_reason",
                "reason IS NULL OR reason IN ('passed','no-tracked-issue','over-byte-cap','gate-failed-open')");
            table.HasCheckConstraint("ck_statement_analysis_runs_outcome",
                "outcome IN ('Drafted','DraftDiscarded','CitationRejected','OutputRejected','Refused','OutputLimitReached','UnexpectedStop','RateLimited','ProviderUnavailable','AuthenticationFailed','ProviderRejected','ConnectionFailed','BillingFailed','ModelNotFound','ProcessingFailed','InvalidResponse','Interrupted','Classified','Bypassed')");
        });
        builder.HasKey(row => row.RunId);
        builder.Property(row => row.RunId).HasColumnName("run_id").HasMaxLength(32);
        builder.Property(row => row.CandidateId).HasColumnName("candidate_id").HasMaxLength(64).IsRequired();
        builder.Property(row => row.Task).HasColumnName("task").HasMaxLength(32).IsRequired();
        builder.Property(row => row.TaskVersion).HasColumnName("task_version").HasMaxLength(128).IsRequired();
        builder.Property(row => row.Model).HasColumnName("model").HasMaxLength(128).IsRequired();
        builder.Property(row => row.PromptVersion).HasColumnName("prompt_version").HasMaxLength(128).IsRequired();
        builder.Property(row => row.SchemaVersion).HasColumnName("schema_version").HasMaxLength(128).IsRequired();
        builder.Property(row => row.InputHash).HasColumnName("input_hash").HasMaxLength(64).IsRequired();
        builder.Property(row => row.IssueRevisionId).HasColumnName("issue_revision_id").HasMaxLength(64);
        builder.Property(row => row.PreviousRunId).HasColumnName("previous_run_id").HasMaxLength(32);
        builder.Property(row => row.Outcome).HasColumnName("outcome").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(row => row.Reason).HasColumnName("reason").HasMaxLength(32);
        builder.Property(row => row.InputTokens).HasColumnName("input_tokens");
        builder.Property(row => row.OutputTokens).HasColumnName("output_tokens");
        builder.Property(row => row.CacheReadTokens).HasColumnName("cache_read_tokens");
        builder.Property(row => row.CacheWriteTokens).HasColumnName("cache_write_tokens");
        builder.Property(row => row.ChargedTokens).HasColumnName("charged_tokens");
        builder.Property(row => row.StopReason).HasColumnName("stop_reason").HasMaxLength(128);
        builder.Property(row => row.ProviderRequestId).HasColumnName("provider_request_id").HasMaxLength(128);
        builder.Property(row => row.OutputJson).HasColumnName("output_json");
        builder.Property(row => row.QuotesJson).HasColumnName("quotes_json");
        builder.Property(row => row.ValidationJson).HasColumnName("validation_json");
        builder.Property(row => row.ContextJson).HasColumnName("context_json");
        builder.Property(row => row.StartedAtUtcTicks).HasColumnName("started_at_utc_ticks");
        builder.Property(row => row.FinishedAtUtcTicks).HasColumnName("finished_at_utc_ticks");
        builder.HasOne<StatementAnalysisRow>().WithMany()
            .HasForeignKey(row => new { row.CandidateId, row.Task }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<StatementAnalysisRunRow>().WithMany()
            .HasForeignKey(row => row.PreviousRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => new { row.CandidateId, row.Task, row.StartedAtUtcTicks });
    }
}
