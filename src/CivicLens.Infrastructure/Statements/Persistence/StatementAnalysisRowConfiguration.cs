using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Statements.Persistence;

internal sealed class StatementAnalysisRowConfiguration : IEntityTypeConfiguration<StatementAnalysisRow>
{
    public void Configure(EntityTypeBuilder<StatementAnalysisRow> builder)
    {
        builder.ToTable("statement_analysis", table =>
        {
            table.HasCheckConstraint("ck_statement_analysis_status",
                "status IN ('Pending','Running','RetryWaiting','WaitingForBudget','Succeeded','Blocked','Failed')");
            table.HasCheckConstraint("ck_statement_analysis_lease", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
            table.HasCheckConstraint("ck_statement_analysis_running", "(status = 'Running') = (lease_token IS NOT NULL)");
            table.HasCheckConstraint("ck_statement_analysis_retry_of", "retry_of_run_id IS NULL OR retry_of_run_id ~ '^[0-9a-f]{32}$'");
            table.HasCheckConstraint("ck_statement_analysis_counts", "fence >= 0 AND attempts >= 0 AND reserved_tokens >= 0");
            table.HasCheckConstraint("ck_statement_analysis_reservation",
                "(reserved_tokens = 0) = (budget_day IS NULL AND reserved_at_utc_ticks IS NULL AND pending_run_id IS NULL AND pending_input_hash IS NULL)");
        });
        builder.HasKey(row => new { row.CandidateId, row.Task });
        builder.Property(row => row.CandidateId).HasColumnName("candidate_id").HasMaxLength(64);
        builder.Property(row => row.Task).HasColumnName("task").HasMaxLength(32);
        builder.Property(row => row.Status).HasColumnName("status").HasMaxLength(32).IsRequired();
        builder.Property(row => row.LeaseToken).HasColumnName("lease_token").HasMaxLength(32);
        builder.Property(row => row.LeaseExpiresAt).HasColumnName("lease_expires_at");
        builder.Property(row => row.Fence).HasColumnName("fence");
        builder.Property(row => row.Attempts).HasColumnName("attempts");
        builder.Property(row => row.RetryAt).HasColumnName("retry_at");
        builder.Property(row => row.ReservedTokens).HasColumnName("reserved_tokens");
        builder.Property(row => row.BudgetDay).HasColumnName("budget_day");
        builder.Property(row => row.ReservedAtUtcTicks).HasColumnName("reserved_at_utc_ticks");
        builder.Property(row => row.PendingRunId).HasColumnName("pending_run_id").HasMaxLength(32);
        builder.Property(row => row.PendingInputHash).HasColumnName("pending_input_hash").HasMaxLength(64);
        builder.Property(row => row.RetryOfRunId).HasColumnName("retry_of_run_id").HasMaxLength(32);
        builder.Property(row => row.ErrorCode).HasColumnName("error_code").HasMaxLength(128);
        builder.Property(row => row.CreatedAtUtcTicks).HasColumnName("created_at_utc_ticks");
        builder.HasOne<StatementCandidateRow>().WithMany()
            .HasForeignKey(row => row.CandidateId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => new { row.Status, row.CreatedAtUtcTicks, row.CandidateId });
    }
}
