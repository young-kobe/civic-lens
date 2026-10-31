using CivicLens.Application.Collection;
using CivicLens.Application.Collection.FederalRegister;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Documents;
using Npgsql;

namespace CivicLens.Tests.Fixtures;

internal sealed class ExecutiveActionSeeder
{
    private readonly string connectionString;
    private readonly PostgresCollectionJobStore jobs;
    private readonly JobExtractionSeeder extractions;

    public ExecutiveActionSeeder(string connectionString, string captureRoot)
    {
        this.connectionString = connectionString;
        jobs = PostgresCollectionJobStore.FromConnectionString(connectionString);
        var documents = PostgresDocumentExtractionStore.FromConnectionString(connectionString);
        Directory.CreateDirectory(captureRoot);
        var candidates = new StatementCandidateSeeder(PostgresCollectionAttemptStore.FromConnectionString(connectionString), documents,
            connectionString, captureRoot);
        extractions = new JobExtractionSeeder(jobs, documents, candidates, connectionString, captureRoot);
    }

    public async Task<string> AdmitAsync(string json, string personId)
    {
        var document = FederalRegisterFixtures.Document(json);
        var source = new ConfiguredCollectionSource(FederalRegisterFixtures.Configuration(), FederalRegisterFixtures.SourceId,
            DateOnly.FromDateTime(DateTime.UtcNow));
        var page = new FederalRegisterListPage([new FederalRegisterListItem(document.DocumentNumber, document.PublishedOn)]);
        var jobId = Assert.Single((await jobs.AdmitListPageAsync(source, page, default)).Jobs).JobId;
        await SucceededJobs.MarkSucceededAsync(connectionString, jobId);
        var text = FederalRegisterRenderer.Render(document);
        var extractionId = await extractions.SaveAsync(jobId, json, text, FederalRegisterDocumentParser.ExtractionVersion);
        await jobs.AdmitDocumentAsync(source, jobId, new FederalRegisterEvidence(document, extractionId, text, personId), default);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT record_id FROM executive_action_drafts LIMIT 1", connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
