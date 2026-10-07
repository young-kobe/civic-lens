using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace CivicLens.Infrastructure.Collection.Persistence;

internal sealed class AttemptRowConfiguration : IEntityTypeConfiguration<AttemptRow>
{
    public void Configure(EntityTypeBuilder<AttemptRow> builder)
    {
        builder.ToTable("collection_attempts", table =>
        {
            table.HasCheckConstraint("ck_collection_attempts_observed_ticks", "observed_at_utc_ticks BETWEEN 0 AND 3155378975999999999");
            table.HasCheckConstraint("ck_collection_attempts_identity", "length(btrim(attempt_id)) > 0 AND length(btrim(source_id)) > 0 AND length(btrim(requested_url)) > 0 AND length(btrim(final_url)) > 0");
            table.HasCheckConstraint("ck_collection_attempts_response_ticks", "response_last_modified_utc_ticks IS NULL OR response_last_modified_utc_ticks BETWEEN 0 AND 3155378975999999999");
            table.HasCheckConstraint("ck_collection_attempts_sent_ticks", "sent_last_modified_utc_ticks IS NULL OR sent_last_modified_utc_ticks BETWEEN 0 AND 3155378975999999999");
            table.HasCheckConstraint("ck_collection_attempts_robots_delay", "robots_crawl_delay_milliseconds IS NULL OR robots_crawl_delay_milliseconds BETWEEN 0 AND 922337203685000");
            table.HasCheckConstraint("ck_collection_attempts_retry_ticks", "retry_delay_ticks IS NULL OR retry_delay_ticks >= 0");
            table.HasCheckConstraint("ck_collection_attempts_outcome", """
                (outcome = 'captured' AND has_response AND response_status_code IS NOT NULL AND response_status_code = 200 AND capture_sha256 IS NOT NULL AND failure_code IS NULL AND retry_delay_ticks IS NULL AND prior_capture_attempt_id IS NULL AND prior_capture_sha256 IS NULL)
                OR (outcome = 'notModified' AND has_response AND has_sent_validators AND response_status_code IS NOT NULL AND response_status_code = 304 AND capture_sha256 IS NULL AND failure_code IS NULL AND retry_delay_ticks IS NULL)
                OR (outcome = 'failed' AND failure_code IS NOT NULL AND retry_delay_ticks IS NULL AND capture_sha256 IS NULL AND prior_capture_attempt_id IS NULL AND prior_capture_sha256 IS NULL)
                OR (outcome = 'deferred' AND failure_code IS NOT NULL AND capture_sha256 IS NULL AND prior_capture_attempt_id IS NULL AND prior_capture_sha256 IS NULL)
                """);
            table.HasCheckConstraint("ck_collection_attempts_response", """
                (has_response AND response_status_code IS NOT NULL AND response_status_code BETWEEN 100 AND 599 AND response_content_encodings IS NOT NULL AND array_position(response_content_encodings, NULL) IS NULL)
                OR (NOT has_response AND response_status_code IS NULL AND response_etag IS NULL AND response_last_modified_utc_ticks IS NULL AND response_content_type IS NULL AND response_content_encodings IS NULL)
                """);
            table.HasCheckConstraint("ck_collection_attempts_validators", """
                (has_sent_validators OR (sent_etag IS NULL AND sent_last_modified_utc_ticks IS NULL))
                """);
            table.HasCheckConstraint("ck_collection_attempts_prior_pair", "(prior_capture_attempt_id IS NULL) = (prior_capture_sha256 IS NULL)");
            table.HasCheckConstraint("ck_collection_attempts_prior_outcome", "prior_capture_attempt_id IS NULL OR (outcome = 'notModified' AND prior_capture_attempt_id <> attempt_id)");
        });

        builder.HasKey(row => row.AttemptId);
        builder.HasIndex(row => new { row.AttemptId, row.CaptureSha256 }).IsUnique()
            .HasDatabaseName("ux_collection_attempts_id_capture");
        builder.Property(row => row.AttemptId).HasColumnName("attempt_id").IsRequired();
        builder.Property(row => row.SourceId).HasColumnName("source_id").IsRequired();
        builder.Property(row => row.RequestedUrl).HasColumnName("requested_url").IsRequired();
        builder.Property(row => row.FinalUrl).HasColumnName("final_url").IsRequired();
        builder.Property(row => row.ObservedAtUtcTicks).HasColumnName("observed_at_utc_ticks").IsRequired();
        builder.Property(row => row.Outcome).HasColumnName("outcome").HasMaxLength(16).IsRequired();
        builder.Property(row => row.HasResponse).HasColumnName("has_response").IsRequired();
        builder.Property(row => row.ResponseStatusCode).HasColumnName("response_status_code");
        builder.Property(row => row.ResponseETag).HasColumnName("response_etag");
        builder.Property(row => row.ResponseLastModifiedUtcTicks).HasColumnName("response_last_modified_utc_ticks");
        builder.Property(row => row.ResponseContentType).HasColumnName("response_content_type");
        builder.Property(row => row.ResponseContentEncodings).HasColumnName("response_content_encodings").HasColumnType("text[]");
        builder.Property(row => row.HasSentValidators).HasColumnName("has_sent_validators").IsRequired();
        builder.Property(row => row.SentETag).HasColumnName("sent_etag");
        builder.Property(row => row.SentLastModifiedUtcTicks).HasColumnName("sent_last_modified_utc_ticks");
        builder.Property(row => row.RobotsCrawlDelayMilliseconds).HasColumnName("robots_crawl_delay_milliseconds");
        builder.Property(row => row.FailureCode).HasColumnName("failure_code");
        builder.Property(row => row.RetryDelayTicks).HasColumnName("retry_delay_ticks");
        builder.Property(row => row.CaptureSha256).HasColumnName("capture_sha256");
        builder.Property(row => row.PriorCaptureAttemptId).HasColumnName("prior_capture_attempt_id");
        builder.Property(row => row.PriorCaptureSha256).HasColumnName("prior_capture_sha256");
        builder.HasOne<CaptureRow>().WithMany().HasForeignKey(row => row.CaptureSha256).OnDelete(DeleteBehavior.Restrict);
        builder.HasOne<AttemptRow>().WithMany().HasForeignKey(row => row.PriorCaptureAttemptId)
            .OnDelete(DeleteBehavior.Restrict);
        builder.HasIndex(row => new { row.SourceId, row.ObservedAtUtcTicks, row.AttemptId })
            .HasDatabaseName("ix_collection_attempts_prior_capture").HasFilter("outcome = 'captured'");
        builder.HasIndex(row => row.CaptureSha256).HasDatabaseName("ix_collection_attempts_capture");
        builder.HasIndex(row => row.PriorCaptureAttemptId).HasDatabaseName("ix_collection_attempts_prior_capture_link");
    }
}
