using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Collection.Processing;

internal sealed class PersistedEvidenceProcessingRowConfiguration : IEntityTypeConfiguration<PersistedEvidenceProcessingRow>
{
    public void Configure(EntityTypeBuilder<PersistedEvidenceProcessingRow> builder)
    {
        builder.ToTable("evidence_processing", table =>
        {
            table.HasCheckConstraint("ck_evidence_processing_fence", "fence >= 0 AND attempts >= 0");
            table.HasCheckConstraint("ck_evidence_processing_lease", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
            table.HasCheckConstraint("ck_evidence_processing_stage", "stage IN ('Preparation','Extraction','Comparison','Complete')");
            table.HasCheckConstraint("ck_evidence_processing_status", "status IN ('Pending','Running','RetryWaiting','WaitingForPredecessor','Succeeded','Blocked','Failed')");
            table.HasCheckConstraint("ck_evidence_processing_evidence_ids", "(extraction_id IS NULL OR extraction_id ~ '^[0-9a-f]{64}$') AND (comparison_id IS NULL OR comparison_id ~ '^[0-9a-f]{64}$')");
            table.HasCheckConstraint("ck_evidence_processing_counts", "admitted_count >= 0 AND deferred_count >= 0 AND duplicate_count >= 0");
        });
        builder.Property(row => row.JobId).HasColumnName("job_id").HasMaxLength(128);
        builder.Property(row => row.AttemptId).HasColumnName("attempt_id").HasMaxLength(128);
        builder.Property(row => row.SourceId).HasColumnName("source_id").HasMaxLength(128);
        builder.Property(row => row.RequestedUrl).HasColumnName("requested_url").HasMaxLength(4096);
        builder.Property(row => row.ObservedAtUtcTicks).HasColumnName("observed_at_utc_ticks");
        builder.Property(row => row.Stage).HasColumnName("stage").HasConversion<string>();
        builder.Property(row => row.Status).HasColumnName("status").HasConversion<string>();
        builder.Property(row => row.LeaseToken).HasColumnName("lease_token").HasMaxLength(64);
        builder.Property(row => row.Fence).HasColumnName("fence");
        builder.Property(row => row.LeaseExpiresAt).HasColumnName("lease_expires_at");
        builder.Property(row => row.Attempts).HasColumnName("attempts");
        builder.Property(row => row.RetryAt).HasColumnName("retry_at");
        builder.Property(row => row.PredecessorAttemptId).HasColumnName("predecessor_attempt_id").HasMaxLength(128);
        builder.Property(row => row.ExtractionId).HasColumnName("extraction_id").HasMaxLength(64);
        builder.Property(row => row.ComparisonId).HasColumnName("comparison_id").HasMaxLength(64);
        builder.Property(row => row.Outcome).HasColumnName("outcome").HasConversion<string>();
        builder.Property(row => row.ErrorCode).HasColumnName("error_code").HasMaxLength(128);
        builder.Property(row => row.AdmittedCount).HasColumnName("admitted_count");
        builder.Property(row => row.DeferredCount).HasColumnName("deferred_count");
        builder.Property(row => row.DuplicateCount).HasColumnName("duplicate_count");
        builder.HasKey(row => new { row.JobId, row.AttemptId });
        builder.HasIndex(row => new { row.JobId, row.Stage, row.Status });
        builder.HasIndex(row => new { row.Status, row.ObservedAtUtcTicks, row.AttemptId, row.JobId });
        builder.HasIndex(row => new { row.PredecessorAttemptId, row.Status });
    }
}
