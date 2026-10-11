using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Analysis.Persistence;

internal sealed class DocumentChangeAnalysisRunRowConfiguration : IEntityTypeConfiguration<DocumentChangeAnalysisRunRow>
{
    public void Configure(EntityTypeBuilder<DocumentChangeAnalysisRunRow> builder)
    {
        builder.ToTable("document_change_analysis_runs", table =>
        {
            table.HasCheckConstraint("ck_document_change_analysis_runs_ids",
                "run_id ~ '^[0-9a-f]{32}$' AND input_hash ~ '^[0-9a-f]{64}$' AND (previous_run_id IS NULL OR previous_run_id ~ '^[0-9a-f]{32}$')");
            table.HasCheckConstraint("ck_document_change_analysis_runs_tokens",
                "input_tokens >= 0 AND output_tokens >= 0 AND cache_read_tokens >= 0 AND cache_write_tokens >= 0 AND " +
                "charged_tokens >= input_tokens + output_tokens + cache_read_tokens + cache_write_tokens");
            table.HasCheckConstraint("ck_document_change_analysis_runs_draft",
                "(draft_id IS NULL AND revision_number IS NULL) OR (draft_id IS NOT NULL AND revision_number = 1)");
            table.HasCheckConstraint("ck_document_change_analysis_runs_times", "finished_at_utc_ticks >= started_at_utc_ticks");
            table.HasCheckConstraint("ck_document_change_analysis_runs_outcome",
                "outcome IN ('Drafted','DraftDiscarded','CitationRejected','OutputRejected','Refused','OutputLimitReached','UnexpectedStop','RateLimited','ProviderUnavailable','AuthenticationFailed','ProviderRejected','ConnectionFailed','BillingFailed','ModelNotFound','ProcessingFailed','InvalidResponse','Interrupted')");
        });
        builder.HasKey(row => row.RunId);
        builder.Property(row => row.RunId).HasColumnName("run_id").HasMaxLength(32);
        builder.Property(row => row.ComparisonId).HasColumnName("comparison_id").HasMaxLength(64).IsRequired();
        builder.Property(row => row.Task).HasColumnName("task").HasMaxLength(32).IsRequired();
        builder.Property(row => row.TaskVersion).HasColumnName("task_version").HasMaxLength(128).IsRequired();
        builder.Property(row => row.Model).HasColumnName("model").HasMaxLength(128).IsRequired();
        builder.Property(row => row.PromptVersion).HasColumnName("prompt_version").HasMaxLength(128).IsRequired();
        builder.Property(row => row.SchemaVersion).HasColumnName("schema_version").HasMaxLength(128).IsRequired();
        builder.Property(row => row.InputHash).HasColumnName("input_hash").HasMaxLength(64).IsRequired();
        builder.Property(row => row.PreviousRunId).HasColumnName("previous_run_id").HasMaxLength(32);
        builder.Property(row => row.Outcome).HasColumnName("outcome").HasConversion<string>().HasMaxLength(32).IsRequired();
        builder.Property(row => row.InputTokens).HasColumnName("input_tokens");
        builder.Property(row => row.OutputTokens).HasColumnName("output_tokens");
        builder.Property(row => row.CacheReadTokens).HasColumnName("cache_read_tokens");
        builder.Property(row => row.CacheWriteTokens).HasColumnName("cache_write_tokens");
        builder.Property(row => row.ChargedTokens).HasColumnName("charged_tokens");
        builder.Property(row => row.StopReason).HasColumnName("stop_reason").HasMaxLength(128);
        builder.Property(row => row.ProviderRequestId).HasColumnName("provider_request_id").HasMaxLength(128);
        builder.Property(row => row.OutputJson).HasColumnName("output_json");
        builder.Property(row => row.ValidationJson).HasColumnName("validation_json");
        builder.Property(row => row.ContextJson).HasColumnName("context_json");
        builder.Property(row => row.DraftId).HasColumnName("draft_id").HasMaxLength(32);
        builder.Property(row => row.RevisionNumber).HasColumnName("revision_number");
        builder.Property(row => row.StartedAtUtcTicks).HasColumnName("started_at_utc_ticks");
        builder.Property(row => row.FinishedAtUtcTicks).HasColumnName("finished_at_utc_ticks");
        builder.HasOne<DocumentChangeAnalysisRow>().WithMany()
            .HasForeignKey(row => new { row.ComparisonId, row.Task }).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<DocumentChangeAnalysisRunRow>().WithMany()
            .HasForeignKey(row => row.PreviousRunId).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<Review.Persistence.DocumentChangeRevisionRow>().WithMany()
            .HasForeignKey(row => new { row.DraftId, row.RevisionNumber }).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => new { row.DraftId, row.RevisionNumber }).IsUnique();
        builder.HasIndex(row => new { row.ComparisonId, row.Task, row.StartedAtUtcTicks });
    }
}
