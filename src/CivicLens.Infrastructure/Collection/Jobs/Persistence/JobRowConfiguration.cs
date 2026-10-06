using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Collection.Jobs.Persistence;

internal sealed class JobRowConfiguration : IEntityTypeConfiguration<JobRow>
{
    public void Configure(EntityTypeBuilder<JobRow> builder)
    {
        builder.ToTable("collection_jobs", table =>
        {
            table.HasCheckConstraint("ck_job_budget", "charged_requests >= 0 AND charged_bytes >= 0 AND charged_seconds >= 0");
            table.HasCheckConstraint("ck_job_lease", "(lease_token IS NULL) = (lease_expires_at IS NULL)");
            table.HasCheckConstraint("ck_job_state", "state IN ('Pending','Running','WaitingToRetry','Succeeded','Failed','Cancelled')");
        });
        builder.Property(row => row.JobId).HasColumnName("job_id");
        builder.Property(row => row.IdempotencyKey).HasColumnName("idempotency_key");
        builder.Property(row => row.DefinitionJson).HasColumnName("definition_json");
        builder.Property(row => row.State).HasColumnName("state");
        builder.Property(row => row.CreatedAt).HasColumnName("created_at");
        builder.Property(row => row.RetryAt).HasColumnName("retry_at");
        builder.Property(row => row.CancellationRequested).HasColumnName("cancellation_requested");
        builder.Property(row => row.ChargedRequests).HasColumnName("charged_requests");
        builder.Property(row => row.ChargedBytes).HasColumnName("charged_bytes");
        builder.Property(row => row.ChargedSeconds).HasColumnName("charged_seconds");
        builder.Property(row => row.LeaseToken).HasColumnName("lease_token");
        builder.Property(row => row.LeaseFence).HasColumnName("lease_fence");
        builder.Property(row => row.LeaseExpiresAt).HasColumnName("lease_expires_at");
        builder.HasKey(row => row.JobId);
        builder.Property(row => row.JobId).HasMaxLength(128);
        builder.Property(row => row.IdempotencyKey).HasMaxLength(256);
        builder.Property(row => row.DefinitionJson).IsRequired();
        builder.Property(row => row.State).HasConversion<string>();
        builder.HasIndex(row => row.IdempotencyKey).IsUnique();
        builder.HasIndex(row => new { row.CreatedAt, row.JobId });
    }
}
