using CivicLens.Application.Collection.Processing;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Processing;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.Processing;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresEvidenceProcessingStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly string schema = "processing_" + Guid.NewGuid().ToString("N");
    private string connectionString = null!;
    private PostgresEvidenceProcessingStore store = null!;

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE SCHEMA {schema}", connection);
        await command.ExecuteNonQueryAsync();
        connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { SearchPath = schema }.ConnectionString;
        store = PostgresEvidenceProcessingStore.FromConnectionString(connectionString);
        await PostgresCollectionAttemptStore.FromConnectionString(connectionString).MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {schema} CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task EnsureIsIdempotentAndCheckpointFencesReclaimedLease()
    {
        await SeedCapturedAttemptAsync("job", "attempt", "senator-page", "https://example.test/page");
        var first = await store.EnsureAsync("job", "attempt", "senator-page", "https://example.test/page", default);
        var replay = await store.EnsureAsync("job", "attempt", "senator-page", "https://example.test/page", default);
        Assert.Equal(first, replay);
        await Assert.ThrowsAsync<InvalidDataException>(() => store.EnsureAsync("job", "attempt", "other-source",
            "https://example.test/page", default));

        var original = await store.TryClaimAsync("job", "attempt", TimeSpan.FromSeconds(30), default);
        Assert.NotNull(original);
        await ExpireLeaseAsync("job", "attempt");
        var recovered = await store.TryClaimAsync("job", "attempt", TimeSpan.FromSeconds(30), default);
        Assert.NotNull(recovered);
        Assert.True(recovered.Fence > original.Fence);

        var stale = new EvidenceProcessingCheckpoint("job", "attempt", EvidenceProcessingStage.Extraction,
            EvidenceProcessingStatus.Running, original.LeaseToken, original.Fence, EvidenceProcessingStage.Comparison,
            EvidenceProcessingStatus.Pending, ExtractionId: ExtractionId("attempt"));
        Assert.False(await store.CheckpointAsync(stale, default));
        var current = stale with { LeaseToken = recovered.LeaseToken, Fence = recovered.Fence };
        var currentRecord = Assert.Single(await store.GetByJobIdsAsync(["job"], default));
        Assert.True(EvidenceProcessingLifecycle.CanTransition(currentRecord, current));
        Assert.True(await store.CheckpointAsync(current, default));
        Assert.False(await store.CheckpointAsync(current, default));

        var records = await store.GetByJobIdsAsync(["job"], default);
        var saved = Assert.Single(records);
        Assert.Equal(EvidenceProcessingStage.Comparison, saved.Stage);
        Assert.Equal(EvidenceProcessingStatus.Pending, saved.Status);
        Assert.Equal(ExtractionId("attempt"), saved.ExtractionId);
        Assert.Equal(0, saved.Attempts);
    }

    [Fact]
    public async Task CompetingClaimsHaveOneOwnerAndWorkOrdersByCaptureObservation()
    {
        await SeedCapturedAttemptAsync("z-newer", "attempt-newer", "senator-page", "https://example.test/page");
        await SeedCapturedAttemptAsync("a-older", "attempt-older", "senator-page", "https://example.test/page");
        await SetObservedAtAsync("attempt-older", DateTimeOffset.UtcNow.AddMinutes(-1).UtcTicks);
        await SetObservedAtAsync("attempt-newer", DateTimeOffset.UtcNow.UtcTicks);
        _ = await store.EnsureAsync("z-newer", "attempt-newer", "senator-page", "https://example.test/page", default);
        _ = await store.EnsureAsync("a-older", "attempt-older", "senator-page", "https://example.test/page", default);

        var eligible = await store.GetEligibleAsync(2, default);
        Assert.Equal(new[] { "attempt-older", "attempt-newer" }, eligible.Select(item => item.AttemptId));
        var claims = await Task.WhenAll(
            store.TryClaimAsync("a-older", "attempt-older", TimeSpan.FromSeconds(30), default),
            store.TryClaimAsync("a-older", "attempt-older", TimeSpan.FromSeconds(30), default));
        Assert.Single(claims, claim => claim is not null);
    }

    [Fact]
    public async Task PreparationBacklogIsBoundedAndReplaysOnlyRetryableMarkers()
    {
        for (var index = 0; index < 4; index++)
            await InsertSuccessfulJobAsync($"job-{index}", DateTimeOffset.UtcNow.UtcTicks + index);
        var backlog = await store.GetPreparationJobIdsAsync(null, 2, default);
        Assert.Equal(new[] { "job-0", "job-1" }, backlog.JobIds);
        Assert.NotNull(backlog.NextCursor);
        var nextBacklog = await store.GetPreparationJobIdsAsync(backlog.NextCursor, 2, default);
        Assert.Equal(new[] { "job-2", "job-3" }, nextBacklog.JobIds);
        foreach (var jobId in backlog.JobIds.Concat(nextBacklog.JobIds))
            await store.EnsurePreparationAsync(jobId, "source", "https://example.test/feed", default);

        await CompletePrepAsync("job-0", EvidenceProcessingOutcome.Prepared, 2, 3, 4);
        await DeferPrepAsync("job-1", DateTimeOffset.UtcNow.AddMinutes(-1));
        await DeferPrepAsync("job-2", DateTimeOffset.UtcNow.AddMinutes(1));
        await DeferPrepAsync("job-3", DateTimeOffset.UtcNow.AddMinutes(-1));

        var eligible = await store.GetEligibleAsync(1, default);
        Assert.Equal("job-1", Assert.Single(eligible).JobId);
        var secondEligible = await store.GetEligibleAsync(2, default);
        Assert.Equal(new[] { "job-1", "job-3" }, secondEligible.Select(row => row.JobId));
        var summary = Assert.Single(await store.GetByJobIdsAsync(["job-0"], default));
        Assert.Equal(2, summary.AdmittedCount);
        Assert.Equal(3, summary.DeferredCount);
        Assert.Equal(4, summary.DuplicateCount);
    }

    private async Task ExpireLeaseAsync(string jobId, string attemptId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE evidence_processing SET lease_expires_at = @expires WHERE job_id = @job AND attempt_id = @attempt", connection);
        command.Parameters.AddWithValue("expires", DateTimeOffset.UtcNow.AddMinutes(-1).UtcTicks);
        command.Parameters.AddWithValue("job", jobId);
        command.Parameters.AddWithValue("attempt", attemptId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task SetObservedAtAsync(string attemptId, long ticks)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE collection_attempts SET observed_at_utc_ticks = @ticks WHERE attempt_id = @attempt", connection);
        command.Parameters.AddWithValue("ticks", ticks);
        command.Parameters.AddWithValue("attempt", attemptId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task CompletePrepAsync(string jobId, EvidenceProcessingOutcome outcome,
        int admitted, int deferred, int duplicates)
    {
        var claim = await store.TryClaimAsync(jobId, "prepare", TimeSpan.FromSeconds(30), default);
        Assert.NotNull(claim);
        var checkpoint = new EvidenceProcessingCheckpoint(jobId, "prepare", EvidenceProcessingStage.Preparation,
            EvidenceProcessingStatus.Running, claim.LeaseToken, claim.Fence, EvidenceProcessingStage.Complete,
            EvidenceProcessingStatus.Succeeded, Outcome: outcome, AdmittedCount: admitted,
            DeferredCount: deferred, DuplicateCount: duplicates);
        Assert.True(await store.CheckpointAsync(checkpoint, default));
    }

    private async Task DeferPrepAsync(string jobId, DateTimeOffset retryAt)
    {
        var claim = await store.TryClaimAsync(jobId, "prepare", TimeSpan.FromSeconds(30), default);
        Assert.NotNull(claim);
        var checkpoint = new EvidenceProcessingCheckpoint(jobId, "prepare", EvidenceProcessingStage.Preparation,
            EvidenceProcessingStatus.Running, claim.LeaseToken, claim.Fence, EvidenceProcessingStage.Preparation,
            EvidenceProcessingStatus.RetryWaiting, ErrorCode: "temporaryStoreFailure", RetryAt: retryAt);
        Assert.True(await store.CheckpointAsync(checkpoint, default));
    }

    private async Task InsertSuccessfulJobAsync(string jobId, long createdAt)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO collection_jobs (job_id, idempotency_key, definition_json, state, created_at,
                cancellation_requested, charged_requests, charged_bytes, charged_seconds, lease_fence)
            VALUES (@id, @key, '{}', 'Succeeded', @created, FALSE, 0, 0, 0, 0)
            """, connection);
        command.Parameters.AddWithValue("id", jobId);
        command.Parameters.AddWithValue("key", "key-" + jobId);
        command.Parameters.AddWithValue("created", createdAt);
        await command.ExecuteNonQueryAsync();
    }

    private async Task SeedCapturedAttemptAsync(string jobId, string attemptId, string sourceId, string url)
    {
        var captureHash = Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
            System.Text.Encoding.UTF8.GetBytes(attemptId)));
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO collection_jobs (job_id, idempotency_key, definition_json, state, created_at,
                cancellation_requested, charged_requests, charged_bytes, charged_seconds, lease_fence)
            VALUES (@job, @key, '{}', 'Succeeded', @created, FALSE, 0, 0, 0, 0);
            INSERT INTO collection_job_attempts (attempt_id, job_id, sequence, request_json, started_at)
            VALUES (@attempt, @job, 1, '{}', @created);
            INSERT INTO collection_captures (sha256, byte_length) VALUES (@hash, 1);
            INSERT INTO collection_attempts (attempt_id, source_id, requested_url, final_url, observed_at_utc_ticks,
                outcome, has_response, response_status_code, response_content_encodings, has_sent_validators, capture_sha256)
            VALUES (@attempt, @source, @url, @url, @created, 'captured', TRUE, 200, ARRAY[]::text[], FALSE, @hash);
            INSERT INTO document_extractions (extraction_id, attempt_id, parser_version, normalization_version, text, text_sha256)
            VALUES (@extraction, @attempt, 'parser-v1', 'normalization-v1', 'text', @text_hash);
            """, connection);
        command.Parameters.AddWithValue("job", jobId);
        command.Parameters.AddWithValue("key", "key-" + jobId);
        command.Parameters.AddWithValue("attempt", attemptId);
        command.Parameters.AddWithValue("source", sourceId);
        command.Parameters.AddWithValue("url", url);
        command.Parameters.AddWithValue("created", DateTimeOffset.UtcNow.UtcTicks);
        command.Parameters.AddWithValue("hash", captureHash);
        command.Parameters.AddWithValue("extraction", ExtractionId(attemptId));
        command.Parameters.AddWithValue("text_hash", new string('c', 64));
        await command.ExecuteNonQueryAsync();
    }

    private static string ExtractionId(string attemptId) => Convert.ToHexStringLower(System.Security.Cryptography.SHA256.HashData(
        System.Text.Encoding.UTF8.GetBytes("extraction/" + attemptId)));
}
