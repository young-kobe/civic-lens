using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Documents;
using Npgsql;

namespace CivicLens.Tests.Fixtures;

internal sealed class JobExtractionSeeder(PostgresCollectionJobStore store, PostgresDocumentExtractionStore extractions,
    StatementCandidateSeeder seeder, string connectionString, string captureRoot)
{
    public async Task<string> SaveAsync(string jobId, string body, string text, string extractionVersion)
    {
        var captured = await AttachCaptureAsync(jobId, body);
        return (await extractions.SaveAsync(new DocumentExtraction(captured, extractionVersion, text), default)).ExtractionId;
    }

    public async Task<CapturedAttemptResult> AttachCaptureAsync(string jobId, string body, DateTimeOffset? observedAt = null)
    {
        var captured = await seeder.ImportAsync(body, "https://example.test/pages/a", CandidateJobKind.None, observedAt: observedAt);
        var request = (await store.GetAsync(jobId, default))!.Definition.CreateRequest(captured.AttemptId, captureRoot);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(JobAttemptSql.Insert($"'{captured.AttemptId}'", $"'{jobId}'",
            $"(SELECT COALESCE(MAX(sequence), 0) + 1 FROM collection_job_attempts WHERE job_id = '{jobId}')", "0", request, completedAt: 0), connection);
        await command.ExecuteNonQueryAsync();
        return captured;
    }
}
