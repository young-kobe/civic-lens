using CivicLens.Collection.Contracts;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Collection.Jobs.Persistence;

internal sealed class JobAttemptRowConfiguration : IEntityTypeConfiguration<JobAttemptRow>
{
    public void Configure(EntityTypeBuilder<JobAttemptRow> builder)
    {
        builder.ToTable("collection_job_attempts", table =>
        {
            table.HasCheckConstraint("ck_job_attempt_sequence", "sequence > 0");
            table.HasCheckConstraint("ck_job_attempt_request_identity", "request_version > 0 AND length(btrim(request_job_id)) > 0 AND length(btrim(request_source_id)) > 0 AND length(btrim(request_url)) > 0");
            table.HasCheckConstraint("ck_job_attempt_request_mode", "request_mode IN ('Page','Feed','Html','Api')");
            table.HasCheckConstraint("ck_job_attempt_request_budgets", "request_max_candidates BETWEEN 1 AND 1000 AND request_max_requests BETWEEN 2 AND 20 AND request_max_bytes BETWEEN 1 AND 100000000 AND request_timeout_seconds BETWEEN 1 AND 300 AND request_min_delay_milliseconds BETWEEN 0 AND 60000");
            table.HasCheckConstraint("ck_job_attempt_request_last_modified_utc_ticks", "request_last_modified_utc_ticks IS NULL OR request_last_modified_utc_ticks BETWEEN 0 AND 3155378975999999999");
            table.HasCheckConstraint("ck_job_attempt_request_credential", "request_credential IS NULL OR request_mode = 'Api'");
            table.HasCheckConstraint("ck_job_attempt_resolution", "(outcome IS NULL) = (completed_at IS NULL) AND (completed_at IS NOT NULL OR error_code IS NULL AND retry_delay_ticks IS NULL AND robots_crawl_delay_milliseconds IS NULL)");
            table.HasCheckConstraint("ck_job_attempt_outcome", "outcome IS NULL OR outcome IN ('Succeeded','RetryableFailure','PermanentFailure','Interrupted')");
            table.HasCheckConstraint("ck_job_attempt_retry_ticks", "retry_delay_ticks IS NULL OR retry_delay_ticks >= 0");
            table.HasCheckConstraint("ck_job_attempt_robots_delay", $"robots_crawl_delay_milliseconds IS NULL OR (robots_crawl_delay_milliseconds BETWEEN 0 AND {CollectionProtocol.MaximumCrawlDelayMilliseconds} AND outcome <> 'Interrupted')");
        });
        builder.Property(row => row.AttemptId).HasColumnName("attempt_id");
        builder.Property(row => row.JobId).HasColumnName("job_id");
        builder.Property(row => row.Sequence).HasColumnName("sequence");
        builder.Property(row => row.RequestVersion).HasColumnName("request_version");
        builder.Property(row => row.RequestJobId).HasColumnName("request_job_id");
        builder.Property(row => row.RequestSourceId).HasColumnName("request_source_id");
        builder.Property(row => row.RequestUrl).HasColumnName("request_url");
        builder.Property(row => row.RequestAllowedOrigin).HasColumnName("request_allowed_origin");
        builder.Property(row => row.RequestAllowedPathPrefix).HasColumnName("request_allowed_path_prefix");
        builder.Property(row => row.RequestArtifactDirectory).HasColumnName("request_artifact_directory");
        builder.Property(row => row.RequestMode).HasColumnName("request_mode").HasConversion<string>().HasMaxLength(16);
        builder.Property(row => row.RequestMaxCandidates).HasColumnName("request_max_candidates");
        builder.Property(row => row.RequestMaxRequests).HasColumnName("request_max_requests");
        builder.Property(row => row.RequestMaxBytes).HasColumnName("request_max_bytes");
        builder.Property(row => row.RequestTimeoutSeconds).HasColumnName("request_timeout_seconds");
        builder.Property(row => row.RequestMinDelayMilliseconds).HasColumnName("request_min_delay_milliseconds");
        builder.Property(row => row.RequestETag).HasColumnName("request_etag");
        builder.Property(row => row.RequestLastModifiedUtcTicks).HasColumnName("request_last_modified_utc_ticks");
        builder.Property(row => row.RequestCredential).HasColumnName("request_credential");
        builder.Property(row => row.StartedAt).HasColumnName("started_at");
        builder.Property(row => row.Outcome).HasColumnName("outcome").HasConversion<string>().HasMaxLength(16);
        builder.Property(row => row.ErrorCode).HasColumnName("error_code");
        builder.Property(row => row.RetryDelayTicks).HasColumnName("retry_delay_ticks");
        builder.Property(row => row.RobotsCrawlDelayMilliseconds).HasColumnName("robots_crawl_delay_milliseconds");
        builder.Property(row => row.CompletedAt).HasColumnName("completed_at");
        builder.HasKey(row => row.AttemptId);
        builder.Property(row => row.AttemptId).HasMaxLength(128);
        builder.HasOne<JobRow>().WithMany().HasForeignKey(row => row.JobId).OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => new { row.JobId, row.Sequence }).IsUnique();
        builder.HasIndex(row => row.JobId).IsUnique().HasFilter("completed_at IS NULL");
    }
}
