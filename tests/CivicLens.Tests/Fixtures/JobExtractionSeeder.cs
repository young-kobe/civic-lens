using System.Text.Json;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Documents;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Documents;
using Npgsql;

namespace CivicLens.Tests.Fixtures;

internal sealed class JobExtractionSeeder(PostgresCollectionJobStore store, PostgresDocumentExtractionStore extractions,
    StatementCandidateSeeder seeder, string connectionString, string captureRoot)
{
    public async Task<string> SaveAsync(string jobId, string body, string text, string parserVersion, string normalizationVersion)
    {
        var captured = await seeder.ImportAsync(body, "https://example.test/pages/a", CandidateJobKind.None);
        var request = (await store.GetAsync(jobId, default))!.Definition.CreateRequest(captured.AttemptId, captureRoot);
        var requestJson = JsonSerializer.Serialize(request, CollectionProtocol.JsonOptions);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($$"""
            INSERT INTO collection_job_attempts (attempt_id, job_id, sequence, request_json, started_at, resolution_json, completed_at)
            VALUES ('{{captured.AttemptId}}', '{{jobId}}',
                    (SELECT COALESCE(MAX(sequence), 0) + 1 FROM collection_job_attempts WHERE job_id = '{{jobId}}'), '{{requestJson}}', 0, '{}', 0)
            """, connection);
        await command.ExecuteNonQueryAsync();
        return (await extractions.SaveAsync(new DocumentExtraction(captured, parserVersion, normalizationVersion, text), default)).ExtractionId;
    }
}
