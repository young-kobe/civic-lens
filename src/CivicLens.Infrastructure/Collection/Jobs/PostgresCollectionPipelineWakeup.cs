using CivicLens.Application;

namespace CivicLens.Infrastructure.Collection.Jobs;

public static class PostgresCollectionPipelineWakeup
{
    private const string Channel = "civic_lens_work";
    private const string Deadlines = """
        SELECT retry_at AS ticks FROM collection_jobs
         WHERE state = 'WaitingToRetry' AND retry_at IS NOT NULL
        UNION ALL
        SELECT lease_expires_at FROM collection_jobs
         WHERE lease_expires_at IS NOT NULL
        UNION ALL
        SELECT retry_at FROM evidence_processing
         WHERE status = 'RetryWaiting' AND retry_at IS NOT NULL
        UNION ALL
        SELECT lease_expires_at FROM evidence_processing
         WHERE lease_expires_at IS NOT NULL
        UNION ALL
        SELECT expires_at FROM collection_collector_slot
         WHERE expires_at IS NOT NULL
        UNION ALL
        SELECT not_before FROM collection_job_origins
        UNION ALL
        SELECT next_due_utc_ticks FROM collection_source_schedules WHERE enabled
        """;

    public static IWorkerWakeup FromConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return new PostgresDeadlineWakeup(connectionString, Channel, Deadlines);
    }
}
