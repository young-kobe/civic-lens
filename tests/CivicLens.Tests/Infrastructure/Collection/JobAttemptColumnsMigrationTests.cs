using System.Text.Json;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Fixtures;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class JobAttemptColumnsMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Migration = "20261103220605_JobAttemptColumns";
    private static readonly DateTimeOffset LastModified = new(2026, 10, 19, 1, 2, 3, TimeSpan.Zero);
    private static readonly TimeSpan RetryDelay = new TimeSpan(1, 2, 3, 4).Add(TimeSpan.FromTicks(1_234_567));

    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateMigratedDatabaseAsync();

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task TheMigrationMovesEveryJsonFieldToItsColumnAndCopiesTheReceiptTransferBecauseTheJsonColumnsGoAway()
    {
        await MigrateToAsync(Before());
        var request = ApiRequest();
        await SeedOldAttemptAsync("with-receipt", request, new OldResolution(CollectionJobAttemptOutcome.RetryableFailure,
            Receipt(request, 3, 77), "RateLimited", RetryDelay, 2500));
        await SeedOldAttemptAsync("without-receipt", Request("without-receipt"), new OldResolution(CollectionJobAttemptOutcome.Succeeded,
            null, "EvidenceAlreadyImported", null, null));
        await SeedOldAttemptAsync("unresolved", Request("unresolved"), null);

        await MigrateToAsync(Migration);

        Assert.Equal(
        [
            "with-receipt|1|request-with-receipt|source|https://example.test/pages/a|https://example.test|/pages|/captures|Api|7|9|1234567|31|1500|\"v7\"|" +
                $"{LastModified.UtcTicks}|congress|RetryableFailure|RateLimited|{RetryDelay.Ticks}|2500|3|77",
            "without-receipt|1|request-without-receipt|source|https://example.test/pages/a|https://example.test|/pages|/captures|Page|100|5|2000000|30|1000|<null>|" +
                "<null>|<null>|Succeeded|EvidenceAlreadyImported|<null>|<null>|<null>|<null>",
            "unresolved|1|request-unresolved|source|https://example.test/pages/a|https://example.test|/pages|/captures|Page|100|5|2000000|30|1000|<null>|" +
                "<null>|<null>|<null>|<null>|<null>|<null>|<null>|<null>"
        ], await ReadAsync("""
            SELECT concat_ws('|', j.attempt_id, j.request_version, j.request_job_id, j.request_source_id, j.request_url, j.request_allowed_origin,
                j.request_allowed_path_prefix, j.request_artifact_directory, j.request_mode, j.request_max_candidates, j.request_max_requests,
                j.request_max_bytes, j.request_timeout_seconds, j.request_min_delay_milliseconds, coalesce(j.request_etag, '<null>'),
                coalesce(j.request_last_modified_utc_ticks::text, '<null>'), coalesce(j.request_credential, '<null>'),
                coalesce(j.outcome, '<null>'), coalesce(j.error_code, '<null>'), coalesce(j.retry_delay_ticks::text, '<null>'),
                coalesce(j.robots_crawl_delay_milliseconds::text, '<null>'), coalesce(a.request_count::text, '<null>'),
                coalesce(a.bytes_received::text, '<null>'))
            FROM collection_job_attempts j LEFT JOIN collection_attempts a ON a.attempt_id = j.attempt_id
            ORDER BY CASE j.attempt_id WHEN 'with-receipt' THEN 1 WHEN 'without-receipt' THEN 2 ELSE 3 END
            """));
        Assert.Empty(await ReadAsync("SELECT 1 FROM information_schema.columns WHERE column_name IN ('request_json', 'resolution_json') AND table_name = 'collection_job_attempts'"));
    }

    [Fact]
    public async Task TheMigrationFailsAndKeepsTheJsonWhenARetryDelayCannotBeReadBecauseDroppingTheJsonWouldLoseTheValue()
    {
        await MigrateToAsync(Before());
        await SeedOldAttemptAsync("negative-delay", Request("negative-delay"), new OldResolution(
            CollectionJobAttemptOutcome.RetryableFailure, null, "RateLimited", TimeSpan.FromSeconds(-1), null));

        await Assert.ThrowsAsync<PostgresException>(() => MigrateToAsync(Migration));

        Assert.Single(await ReadAsync("SELECT 1 FROM information_schema.columns WHERE column_name = 'request_json' AND table_name = 'collection_job_attempts'"));
    }

    [Fact]
    public async Task TheMigrationFailsAndKeepsTheJsonWhenAReceiptTransferHasNoAttemptRowBecauseDroppingTheJsonWouldLoseTheTransfer()
    {
        await MigrateToAsync(Before());
        var request = Request("orphan-receipt");
        await SeedOldAttemptAsync("orphan-receipt", request, new OldResolution(
            CollectionJobAttemptOutcome.PermanentFailure, Receipt(request, 3, 77), "HttpError", null, null), importEvidence: false);

        await Assert.ThrowsAsync<PostgresException>(() => MigrateToAsync(Migration));

        Assert.Single(await ReadAsync("SELECT 1 FROM information_schema.columns WHERE column_name = 'request_json' AND table_name = 'collection_job_attempts'"));
    }

    [Fact]
    public async Task TheRollbackRebuildsJsonThatReadsBackAsTheSameRequestAndResolutionBecauseARollbackMustLoseNothingTheColumnsHold()
    {
        await MigrateToAsync(Before());
        var request = ApiRequest();
        var resolution = new OldResolution(CollectionJobAttemptOutcome.RetryableFailure, Receipt(request, 3, 77), "RateLimited", RetryDelay, 2500);
        await SeedOldAttemptAsync("with-receipt", request, resolution);
        await SeedOldAttemptAsync("unresolved", Request("unresolved"), null);
        await MigrateToAsync(Migration);
        var columns = await ReadColumnsAsync();

        await MigrateToAsync(Before());

        var rebuilt = await ReadAsync("SELECT request_json FROM collection_job_attempts WHERE attempt_id = 'with-receipt'");
        Assert.Equal(request, JsonSerializer.Deserialize<CollectionRequest>(Assert.Single(rebuilt), CollectionProtocol.JsonOptions));
        var restored = JsonSerializer.Deserialize<OldResolution>(
            Assert.Single(await ReadAsync("SELECT resolution_json FROM collection_job_attempts WHERE attempt_id = 'with-receipt'")), CollectionProtocol.JsonOptions)!;
        Assert.Equal(resolution with { Receipt = null }, restored);
        Assert.Equal(Request("unresolved"), JsonSerializer.Deserialize<CollectionRequest>(
            Assert.Single(await ReadAsync("SELECT request_json FROM collection_job_attempts WHERE attempt_id = 'unresolved'")), CollectionProtocol.JsonOptions));
        Assert.Single(await ReadAsync("SELECT 1 FROM collection_job_attempts WHERE resolution_json IS NULL AND completed_at IS NULL"));
        Assert.Empty(await ReadAsync("SELECT 1 FROM information_schema.columns WHERE table_name = 'collection_attempts' AND column_name IN ('request_count', 'bytes_received')"));

        await MigrateToAsync(Migration);

        Assert.Equal(columns.Select(row => row.Replace("|3|77", "|<null>|<null>", StringComparison.Ordinal)), await ReadColumnsAsync());
    }

    [Theory]
    [InlineData("job_attempt_request_identity", "request_url = ' '")]
    [InlineData("job_attempt_request_identity", "request_allowed_origin = ' '")]
    [InlineData("job_attempt_request_identity", "request_allowed_path_prefix = ' '")]
    [InlineData("job_attempt_request_identity", "request_artifact_directory = ' '")]
    [InlineData("job_attempt_request_identity", "request_version = 2")]
    [InlineData("job_attempt_request_mode", "request_mode = 'Other'")]
    [InlineData("job_attempt_request_budgets", "request_max_requests = 1")]
    [InlineData("job_attempt_request_last_modified_utc_ticks", "request_last_modified_utc_ticks = -1")]
    [InlineData("job_attempt_request_credential", "request_credential = 'congress'")]
    [InlineData("job_attempt_resolution", "completed_at = NULL")]
    [InlineData("job_attempt_resolution", "error_code = 'Unexpected', outcome = NULL, completed_at = NULL")]
    [InlineData("job_attempt_outcome", "outcome = 'Unknown'")]
    [InlineData("job_attempt_retry_ticks", "retry_delay_ticks = -1")]
    [InlineData("job_attempt_robots_delay", "robots_crawl_delay_milliseconds = -1")]
    [InlineData("job_attempt_robots_delay", "outcome = 'Interrupted', robots_crawl_delay_milliseconds = 5")]
    public async Task ARowThatBreaksARuleOfTheJobAttemptIsRefusedByItsNamedCheckBecauseTheColumnsAreTheOnlyCopy(string constraint, string change)
    {
        await ExecuteAsync(JobsInsert + JobAttemptSql.Insert("'a'", "'job'", "1", "0", completedAt: 1) + ";");

        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync($"UPDATE collection_job_attempts SET {change} WHERE attempt_id = 'a'"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
        Assert.Equal("ck_" + constraint, failure.ConstraintName);
    }

    [Theory]
    [InlineData("request_count = 1, bytes_received = NULL")]
    [InlineData("request_count = NULL, bytes_received = 1")]
    [InlineData("request_count = -1, bytes_received = 0")]
    [InlineData("request_count = 0, bytes_received = 1")]
    public async Task AnAttemptTransferThatBreaksARuleIsRefusedBecauseBothMeasuresMustBeRecordedTogetherAndRealistic(string change)
    {
        await ExecuteAsync("""
            INSERT INTO collection_attempts (attempt_id, source_id, requested_url, final_url, observed_at_utc_ticks, outcome, has_response,
                has_sent_validators, failure_code, elapsed_milliseconds)
            VALUES ('a', 'source', 'https://example.test/a', 'https://example.test/a', 1, 'failed', FALSE, FALSE, 'HttpError', 0)
            """);

        var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync($"UPDATE collection_attempts SET {change} WHERE attempt_id = 'a'"));

        Assert.Equal("ck_collection_attempts_transfer", failure.ConstraintName);
    }

    private const string JobsInsert = """
        INSERT INTO collection_jobs (job_id, idempotency_key, definition_json, source_id, is_root, pacing_key, state, created_at,
            cancellation_requested, lease_fence)
        VALUES ('job', 'key', '{}', 'source', FALSE, 'source', 'Succeeded', 0, FALSE, 0);
        """;

    private static CollectionRequest Request(string name) => CollectionFixtures.Request() with
    {
        JobId = "request-" + name,
        ArtifactDirectory = "/captures"
    };

    private static CollectionRequest ApiRequest() => Request("with-receipt") with
    {
        Mode = CollectionMode.Api,
        Credential = "congress",
        MaxCandidates = 7,
        MaxRequests = 9,
        MaxBytes = 1_234_567,
        TimeoutSeconds = 31,
        MinDelayMilliseconds = 1500,
        ETag = "\"v7\"",
        LastModified = LastModified
    };

    private static CollectionResult Receipt(CollectionRequest request, int requests, long bytes) => CollectionFixtures.Receipt(request) with
    {
        Outcome = CollectionOutcome.Failed,
        Capture = null,
        FailureCode = CollectionFailureCode.HttpError,
        RequestCount = requests,
        BytesReceived = bytes
    };

    private async Task SeedOldAttemptAsync(string attemptId, CollectionRequest request, OldResolution? resolution,
        bool importEvidence = true)
    {
        var jobId = "job-" + attemptId;
        var requestJson = Quote(JsonSerializer.Serialize(request, CollectionProtocol.JsonOptions));
        var resolutionJson = resolution is null ? "NULL" : Quote(JsonSerializer.Serialize(resolution, CollectionProtocol.JsonOptions));
        await ExecuteAsync($$"""
            INSERT INTO collection_jobs (job_id, idempotency_key, definition_json, source_id, is_root, pacing_key, state, created_at,
                cancellation_requested, lease_fence)
            VALUES ('{{jobId}}', 'key-{{attemptId}}', '{}', 'source', FALSE, 'source', 'Succeeded', 0, FALSE, 0);
            INSERT INTO collection_job_attempts (attempt_id, job_id, sequence, request_json, started_at, resolution_json, completed_at)
            VALUES ('{{attemptId}}', '{{jobId}}', 1, {{requestJson}}, 0, {{resolutionJson}}, {{(resolution is null ? "NULL" : "1")}});
            """);
        if (resolution is null || !importEvidence) return;
        await ExecuteAsync($"""
            INSERT INTO collection_attempts (attempt_id, source_id, requested_url, final_url, observed_at_utc_ticks, outcome, has_response,
                has_sent_validators, failure_code)
            VALUES ('{attemptId}', 'source', 'https://example.test/pages/a', 'https://example.test/pages/a', 1, 'failed', FALSE, FALSE, 'HttpError')
            """);
    }

    private Task<List<string>> ReadColumnsAsync() => ReadAsync("""
        SELECT concat_ws('|', j.attempt_id, j.request_version, j.request_job_id, coalesce(j.request_etag, '<null>'),
            coalesce(j.request_last_modified_utc_ticks::text, '<null>'), coalesce(j.request_credential, '<null>'),
            coalesce(j.outcome, '<null>'), coalesce(j.error_code, '<null>'), coalesce(j.retry_delay_ticks::text, '<null>'),
            coalesce(j.robots_crawl_delay_milliseconds::text, '<null>'), coalesce(a.request_count::text, '<null>'),
            coalesce(a.bytes_received::text, '<null>'))
        FROM collection_job_attempts j LEFT JOIN collection_attempts a ON a.attempt_id = j.attempt_id ORDER BY j.attempt_id DESC
        """);

    private static string Quote(string value) => "'" + value.Replace("'", "''", StringComparison.Ordinal) + "'";

    private string Before() => NewContext().Database.GetMigrations().TakeWhile(id => id != Migration).Last();

    private CollectionAttemptDbContext NewContext() =>
        new(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);

    private async Task MigrateToAsync(string target)
    {
        await using var db = NewContext();
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<string>> ReadAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(reader.GetValue(0).ToString()!);
        return rows;
    }

    private sealed record OldResolution(CollectionJobAttemptOutcome Outcome, CollectionResult? Receipt, string? ErrorCode,
        TimeSpan? RetryDelay, long? RobotsCrawlDelayMilliseconds);
}
