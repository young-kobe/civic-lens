using System.Security.Cryptography;
using System.Text;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class StatementCandidateQueueMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string PreviousMigration = "20261012150000_StoreReviewStatusAndHasChanges";
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateEmptyDatabaseAsync();
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task BackfillRegistersOneCandidateForEachAdmittedExtractedArticleFromItsFirstObservedExtraction()
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>()
            .UseNpgsql(connectionString).Options);
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await ExecuteAsync(connectionString, "SET session_replication_role = replica;" +
            AdmittedArticle("job-a1", "attempt-a1", observed: 100, extractionId: Hex('f'), "/a", candidateJob: "job-a2") +
            RecheckOfArticle("job-a2", "attempt-a2", observed: 200, extractionId: Hex('1'), "/a") +
            ExtractionOf("attempt-a2", Hex('2')) +
            AdmittedArticle("job-b", "attempt-b", observed: 300, extractionId: null, "/b", candidateJob: "job-b") +
            WatchedPage("job-w", "attempt-w", observed: 50, extractionId: Hex('3'), "/w"));

        await db.Database.MigrateAsync();

        var candidates = await ReadAsync("SELECT candidate_id, extraction_id, attempt_id FROM statement_candidates");
        Assert.Equal([[CandidateHash("source", "https://example.test/pages/a"), Hex('f'), "attempt-a1"]], candidates);
        var gates = await ReadAsync("SELECT task, status, fence, attempts, reserved_tokens FROM statement_analysis");
        Assert.Equal([["gate", "Pending", "0", "0", "0"]], gates);
    }

    private static string Hex(char digit) => new(digit, 64);

    private static string CandidateHash(string sourceId, string url) =>
        Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes($"{sourceId.Length}:{sourceId}{url}")));

    private static string AdmittedArticle(string jobId, string attemptId, long observed, string? extractionId, string path,
        string candidateJob) =>
        Job(jobId, "discovery-admission/" + jobId) + Attempt(jobId, attemptId, observed, path) +
        $"INSERT INTO collection_candidate_jobs (source_id, candidate_hash, url, job_id) VALUES ('source', '{CandidateHash("source", "https://example.test/pages" + path)}', 'https://example.test/pages{path}', '{candidateJob}') ON CONFLICT DO NOTHING;" +
        (extractionId is null ? "" : ExtractionOf(attemptId, extractionId));

    private static string RecheckOfArticle(string jobId, string attemptId, long observed, string extractionId, string path) =>
        Job(jobId, "discovery-admission/" + jobId) + Attempt(jobId, attemptId, observed, path) + ExtractionOf(attemptId, extractionId);

    private static string WatchedPage(string jobId, string attemptId, long observed, string extractionId, string path) =>
        Job(jobId, "watched-page/" + jobId) + Attempt(jobId, attemptId, observed, path) + ExtractionOf(attemptId, extractionId) +
        $"INSERT INTO collection_candidate_jobs (source_id, candidate_hash, url, job_id) VALUES ('source', '{CandidateHash("source", "https://example.test/pages" + path)}', 'https://example.test/pages{path}', '{jobId}');";

    private static string Job(string jobId, string key) => $$"""
        INSERT INTO collection_jobs (job_id, idempotency_key, definition_json, state, created_at, cancellation_requested,
            charged_requests, charged_bytes, charged_seconds, lease_fence)
        VALUES ('{{jobId}}', '{{key}}', '{"sourceId":"source"}', 'Succeeded', 0, false, 0, 0, 0, 0);
        """;

    private static string Attempt(string jobId, string attemptId, long observed, string path) => $$"""
        INSERT INTO collection_attempts (attempt_id, source_id, requested_url, final_url, observed_at_utc_ticks, outcome,
            has_response, response_status_code, response_content_encodings, has_sent_validators, capture_sha256)
        VALUES ('{{attemptId}}', 'source', 'https://example.test/pages{{path}}', 'https://example.test/pages{{path}}', {{observed}},
            'captured', true, 200, '{}', false, '{{Hex('c')}}');
        INSERT INTO collection_job_attempts (attempt_id, job_id, sequence, request_json, started_at)
        VALUES ('{{attemptId}}', '{{jobId}}', 1, '{}', 0);
        """;

    private static string ExtractionOf(string attemptId, string extractionId) => $$"""
        INSERT INTO document_extractions (extraction_id, attempt_id, parser_version, normalization_version, text, text_sha256)
        VALUES ('{{extractionId}}', '{{attemptId}}', 'p', 'n', 'text', '{{Hex('d')}}');
        """;

    private async Task<List<string[]>> ReadAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string[]>();
        while (await reader.ReadAsync())
            rows.Add([.. Enumerable.Range(0, reader.FieldCount).Select(index => Convert.ToString(reader.GetValue(index))!)]);
        return rows;
    }

    private static async Task ExecuteAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
