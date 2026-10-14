using CivicLens.Application.Analysis;
using CivicLens.Core.Analysis;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Analysis.Persistence;

internal static class AnalysisTableConfiguration
{
    public static void ConfigureQueue<TRow>(EntityTypeBuilder<TRow> builder, string table, string keyProperty, string keyColumn)
        where TRow : class, IAnalysisRow
    {
        builder.ToTable(table, constraints =>
        {
            constraints.HasCheckConstraint($"ck_{table}_status",
                "status IN ('Pending','Running','RetryWaiting','WaitingForBudget','Succeeded','Blocked','Failed')");
            constraints.HasCheckConstraint($"ck_{table}_lease", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
            constraints.HasCheckConstraint($"ck_{table}_running", "(status = 'Running') = (lease_token IS NOT NULL)");
            constraints.HasCheckConstraint($"ck_{table}_retry_of", "retry_of_run_id IS NULL OR retry_of_run_id ~ '^[0-9a-f]{32}$'");
            constraints.HasCheckConstraint($"ck_{table}_counts", "fence >= 0 AND attempts >= 0 AND reserved_tokens >= 0");
            constraints.HasCheckConstraint($"ck_{table}_reservation",
                "(reserved_tokens = 0) = (budget_day IS NULL AND reserved_at_utc_ticks IS NULL AND pending_run_id IS NULL AND pending_input_hash IS NULL)");
        });
        builder.HasKey(keyProperty, nameof(IAnalysisRow.Task));
        builder.Property<string>(keyProperty).HasColumnName(keyColumn).HasMaxLength(64);
        builder.Property<string>(nameof(IAnalysisRow.Task)).HasColumnName("task").HasMaxLength(32);
        builder.Property<AnalysisState>(nameof(IAnalysisRow.Status)).HasColumnName("status").HasConversion<string>().HasMaxLength(32)
            .IsRequired();
        builder.Property<string>(nameof(IAnalysisRow.LeaseToken)).HasColumnName("lease_token").HasMaxLength(32);
        builder.Property<long?>(nameof(IAnalysisRow.LeaseExpiresAt)).HasColumnName("lease_expires_at");
        builder.Property<long>(nameof(IAnalysisRow.Fence)).HasColumnName("fence");
        builder.Property<int>(nameof(IAnalysisRow.Attempts)).HasColumnName("attempts");
        builder.Property<long?>(nameof(IAnalysisRow.RetryAt)).HasColumnName("retry_at");
        builder.Property<long>(nameof(IAnalysisRow.ReservedTokens)).HasColumnName("reserved_tokens");
        builder.Property<DateOnly?>(nameof(IAnalysisRow.BudgetDay)).HasColumnName("budget_day");
        builder.Property<long?>(nameof(IAnalysisRow.ReservedAtUtcTicks)).HasColumnName("reserved_at_utc_ticks");
        builder.Property<string>(nameof(IAnalysisRow.PendingRunId)).HasColumnName("pending_run_id").HasMaxLength(32);
        builder.Property<string>(nameof(IAnalysisRow.PendingInputHash)).HasColumnName("pending_input_hash").HasMaxLength(64);
        builder.Property<string>(nameof(IAnalysisRow.RetryOfRunId)).HasColumnName("retry_of_run_id").HasMaxLength(32);
        builder.Property<string>(nameof(IAnalysisRow.ErrorCode)).HasColumnName("error_code").HasMaxLength(128);
        builder.Property<long>(nameof(IAnalysisRow.CreatedAtUtcTicks)).HasColumnName("created_at_utc_ticks");
        builder.HasIndex(nameof(IAnalysisRow.Status), nameof(IAnalysisRow.CreatedAtUtcTicks), keyProperty);
        builder.HasIndex([nameof(IAnalysisRow.Task), nameof(IAnalysisRow.CreatedAtUtcTicks), keyProperty], $"ix_{table}_active_claim")
            .HasFilter("status IN ('Pending','RetryWaiting','WaitingForBudget','Running')");
    }

    public static void ConfigureRun<TRow>(EntityTypeBuilder<TRow> builder, string table, string keyProperty, string keyColumn,
        Action<TableBuilder<TRow>> featureConstraints) where TRow : class, IAnalysisRunRow
    {
        builder.ToTable(table, constraints =>
        {
            constraints.HasCheckConstraint($"ck_{table}_tokens",
                "input_tokens >= 0 AND output_tokens >= 0 AND cache_read_tokens >= 0 AND cache_write_tokens >= 0 AND " +
                "charged_tokens >= input_tokens + output_tokens + cache_read_tokens + cache_write_tokens");
            constraints.HasCheckConstraint($"ck_{table}_times", "finished_at_utc_ticks >= started_at_utc_ticks");
            featureConstraints(constraints);
        });
        builder.HasKey(nameof(IAnalysisRunRow.RunId));
        builder.Property<string>(nameof(IAnalysisRunRow.RunId)).HasColumnName("run_id").HasMaxLength(32);
        builder.Property<string>(keyProperty).HasColumnName(keyColumn).HasMaxLength(64).IsRequired();
        builder.Property<string>(nameof(IAnalysisRunRow.Task)).HasColumnName("task").HasMaxLength(32).IsRequired();
        builder.Property<string>(nameof(IAnalysisRunRow.TaskVersion)).HasColumnName("task_version").HasMaxLength(128).IsRequired();
        builder.Property<string>(nameof(IAnalysisRunRow.Model)).HasColumnName("model").HasMaxLength(128).IsRequired();
        builder.Property<string>(nameof(IAnalysisRunRow.PromptVersion)).HasColumnName("prompt_version").HasMaxLength(128).IsRequired();
        builder.Property<string>(nameof(IAnalysisRunRow.SchemaVersion)).HasColumnName("schema_version").HasMaxLength(128).IsRequired();
        builder.Property<string>(nameof(IAnalysisRunRow.InputHash)).HasColumnName("input_hash").HasMaxLength(64).IsRequired();
        builder.Property<string>(nameof(IAnalysisRunRow.PreviousRunId)).HasColumnName("previous_run_id").HasMaxLength(32);
        builder.Property<AnalysisRunOutcome>(nameof(IAnalysisRunRow.Outcome)).HasColumnName("outcome").HasConversion<string>()
            .HasMaxLength(32).IsRequired();
        builder.Property<long>(nameof(IAnalysisRunRow.InputTokens)).HasColumnName("input_tokens");
        builder.Property<long>(nameof(IAnalysisRunRow.OutputTokens)).HasColumnName("output_tokens");
        builder.Property<long>(nameof(IAnalysisRunRow.CacheReadTokens)).HasColumnName("cache_read_tokens");
        builder.Property<long>(nameof(IAnalysisRunRow.CacheWriteTokens)).HasColumnName("cache_write_tokens");
        builder.Property<long>(nameof(IAnalysisRunRow.ChargedTokens)).HasColumnName("charged_tokens");
        builder.Property<string>(nameof(IAnalysisRunRow.StopReason)).HasColumnName("stop_reason").HasMaxLength(128);
        builder.Property<string>(nameof(IAnalysisRunRow.ProviderRequestId)).HasColumnName("provider_request_id").HasMaxLength(128);
        builder.Property<string>(nameof(IAnalysisRunRow.OutputJson)).HasColumnName("output_json");
        builder.Property<string>(nameof(IAnalysisRunRow.ValidationJson)).HasColumnName("validation_json");
        builder.Property<string>(nameof(IAnalysisRunRow.ContextJson)).HasColumnName("context_json");
        builder.Property<long>(nameof(IAnalysisRunRow.StartedAtUtcTicks)).HasColumnName("started_at_utc_ticks");
        builder.Property<long>(nameof(IAnalysisRunRow.FinishedAtUtcTicks)).HasColumnName("finished_at_utc_ticks");
        builder.HasOne<TRow>().WithMany().HasForeignKey(nameof(IAnalysisRunRow.PreviousRunId)).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(keyProperty, nameof(IAnalysisRunRow.Task), nameof(IAnalysisRunRow.StartedAtUtcTicks));
    }
}
