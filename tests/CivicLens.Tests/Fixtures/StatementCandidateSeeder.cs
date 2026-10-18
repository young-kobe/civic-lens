using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Documents;
using Npgsql;
using static CivicLens.Tests.Fixtures.CollectionFixtures;

namespace CivicLens.Tests.Fixtures;

internal enum CandidateJobKind { None, DiscoveryAdmitted, WatchedPage, BillStream }

internal sealed class StatementCandidateSeeder(PostgresCollectionAttemptStore attempts, PostgresDocumentExtractionStore extractions,
    string connectionString, string captureRoot)
{
    public async Task<string> SeedAsync(string text, string url, CollectionJobDefinition? admittedBy = null)
    {
        var definition = admittedBy ?? StatementDraftFixtures.AdmittedDefinition(StatementDraftFixtures.SoleCoverage(StatementDraftFixtures.Warren));
        var captured = await ImportAsync(text, url, CandidateJobKind.DiscoveryAdmitted, definitionJson: JsonSerializer.Serialize(definition with { ConfigurationRevision = null }, CollectionProtocol.JsonOptions),
            revisionId: await StoreRevisionAsync(definition.ConfigurationRevision));
        await extractions.SaveAsync(new DocumentExtraction(captured, "parser", "normalizer", text), CancellationToken.None);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT candidate_id FROM statement_candidates WHERE url = @url", connection);
        command.Parameters.AddWithValue("url", url);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private async Task<string?> StoreRevisionAsync(CollectionConfigurationRevision? revision)
    {
        if (revision is null) return null;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO collection_configuration_revisions (revision_id, json, created_at)
            VALUES (@id, @json, 0) ON CONFLICT (revision_id) DO NOTHING
            """, connection);
        command.Parameters.AddWithValue("id", revision.Id);
        command.Parameters.AddWithValue("json", revision.Json);
        await command.ExecuteNonQueryAsync();
        return revision.Id;
    }

    public async Task<CapturedAttemptResult> ImportAsync(string text, string url, CandidateJobKind kind, string sourceId = "source",
        string definitionJson = "{}", string? revisionId = null, DateTimeOffset? observedAt = null)
    {
        var bytes = Encoding.UTF8.GetBytes(text);
        var hash = Convert.ToHexStringLower(SHA256.HashData(bytes));
        await using (var file = File.Create(Path.Combine(captureRoot, hash + ".gz")))
        await using (var gzip = new GZipStream(file, CompressionLevel.SmallestSize))
            await gzip.WriteAsync(bytes);
        var attemptId = Guid.NewGuid().ToString("N");
        var request = Request() with { JobId = attemptId, SourceId = sourceId, Url = url, ArtifactDirectory = captureRoot };
        var receipt = Receipt(request) with
        {
            ObservedAt = observedAt ?? DateTimeOffset.UtcNow,
            BytesReceived = bytes.Length,
            Capture = new CaptureArtifact { Sha256 = hash, RelativePath = hash + ".gz", ByteLength = bytes.Length },
            Response = new HttpResponseMetadata { StatusCode = 200, ContentType = "text/plain; charset=utf-8", ContentEncodings = [] }
        };
        var imported = await attempts.ImportAtomicallyAsync(CollectionAttemptImporter.CreateImport(attemptId, request, receipt),
            CancellationToken.None);
        if (kind != CandidateJobKind.None) await SeedJobAttemptAsync(attemptId, kind, sourceId, definitionJson, revisionId);
        return (CapturedAttemptResult)imported.AttemptResult;
    }

    private async Task SeedJobAttemptAsync(string attemptId, CandidateJobKind kind, string sourceId,
        string definitionJson, string? revisionId)
    {
        var prefix = kind switch
        {
            CandidateJobKind.DiscoveryAdmitted => "discovery-admission/",
            CandidateJobKind.BillStream => BillStreamJobKeys.Prefix,
            _ => "watched-page/"
        };
        var key = prefix + attemptId;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            INSERT INTO collection_jobs (job_id, idempotency_key, definition_json, source_id, is_root,
                configuration_revision_id, state, created_at, cancellation_requested, lease_fence)
            VALUES (@id, @key, @definition, @source, false, @revision, 'Succeeded', 0, false, 0);
            INSERT INTO collection_job_attempts (attempt_id, job_id, sequence, request_json, started_at)
            VALUES (@id, @id, 1, '{}', 0);
            """, connection);
        command.Parameters.AddWithValue("id", attemptId);
        command.Parameters.AddWithValue("key", key);
        command.Parameters.AddWithValue("definition", definitionJson);
        command.Parameters.AddWithValue("source", sourceId);
        command.Parameters.AddWithValue("revision", (object?)revisionId ?? DBNull.Value);
        await command.ExecuteNonQueryAsync();
    }
}
