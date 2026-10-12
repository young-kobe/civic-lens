using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Documents;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using Npgsql;
using static CivicLens.Tests.Fixtures.CollectionFixtures;

namespace CivicLens.Tests.Fixtures;

internal sealed class StatementCandidateSeeder(PostgresCollectionAttemptStore attempts, PostgresDocumentExtractionStore extractions,
    string connectionString, string captureRoot)
{
    public async Task<string> SeedAsync(string text, string url)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        await using (var file = File.Create(Path.Combine(captureRoot, hash + ".gz")))
        await using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
            await gzip.WriteAsync(bytes);
        var attemptId = Guid.NewGuid().ToString("N");
        var request = Request() with { JobId = attemptId, SourceId = "source", Url = url, ArtifactDirectory = captureRoot };
        var receipt = Receipt(request) with
        {
            BytesReceived = bytes.Length,
            Capture = new CaptureArtifact { Sha256 = hash, RelativePath = hash + ".gz", ByteLength = bytes.Length },
            Response = new HttpResponseMetadata { StatusCode = 200, ContentType = "text/plain; charset=utf-8", ContentEncodings = [] }
        };
        var imported = await attempts.ImportAtomicallyAsync(CollectionAttemptImporter.CreateImport(attemptId, request, receipt),
            CancellationToken.None);
        await ExecuteAsync($$"""
            INSERT INTO collection_jobs (job_id, idempotency_key, definition_json, state, created_at, cancellation_requested,
                charged_requests, charged_bytes, charged_seconds, lease_fence)
            VALUES ('{{attemptId}}', 'discovery-admission/{{attemptId}}', '{}', 'Succeeded', 0, false, 0, 0, 0, 0);
            INSERT INTO collection_job_attempts (attempt_id, job_id, sequence, request_json, started_at)
            VALUES ('{{attemptId}}', '{{attemptId}}', 1, '{}', 0);
            """);
        var captured = (CivicLens.Core.Collection.CapturedAttemptResult)imported.AttemptResult;
        await extractions.SaveAsync(new DocumentExtraction(captured, "parser", "normalizer", text), CancellationToken.None);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT candidate_id FROM statement_candidates WHERE url = @url", connection);
        command.Parameters.AddWithValue("url", url);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
