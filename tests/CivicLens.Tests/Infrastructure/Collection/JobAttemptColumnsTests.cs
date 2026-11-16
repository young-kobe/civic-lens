using CivicLens.Tests.Fixtures;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class JobAttemptColumnsTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateMigratedDatabaseAsync();

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

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

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
