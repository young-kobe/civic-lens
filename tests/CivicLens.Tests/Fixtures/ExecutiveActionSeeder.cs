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
        jobs = PostgresCollectionJobStore.FromConnectionString(connectionString, TestCodeVersion.Current);
        var documents = PostgresDocumentExtractionStore.FromConnectionString(connectionString);
        Directory.CreateDirectory(captureRoot);
        var candidates = new StatementCandidateSeeder(PostgresCollectionAttemptStore.FromConnectionString(connectionString), documents,
            connectionString, captureRoot);
        extractions = new JobExtractionSeeder(jobs, documents, candidates, connectionString, captureRoot);
    }

    public async Task<string> AdmitAsync(string json, string personId, string shiftedStoredTextPrefix = "")
    {
        var document = FederalRegisterFixtures.Document(json);
        var source = new ConfiguredCollectionSource(FederalRegisterFixtures.Configuration(), FederalRegisterFixtures.SourceId,
            DateOnly.FromDateTime(DateTime.UtcNow));
        var page = new FederalRegisterListPage([new FederalRegisterListItem(document.DocumentNumber, document.PublishedOn)]);
        var jobId = Assert.Single((await jobs.AdmitListPageAsync(source, page, default)).Jobs).JobId;
        await SucceededJobs.MarkSucceededAsync(connectionString, jobId);
        var text = FederalRegisterRenderer.Render(document);
        var extractionId = await extractions.SaveAsync(jobId, json, shiftedStoredTextPrefix + text, FederalRegisterDocumentParser.ExtractionVersion);
        await jobs.AdmitDocumentAsync(source, jobId, new FederalRegisterEvidence(document, extractionId, text, personId), default);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT record_id FROM executive_action_drafts LIMIT 1", connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    /// <summary>Admits a document that has a pinned body URL, then its text job and the text. The text row and the summary queue entry come from
    /// <see cref="PostgresCollectionJobStore.AdmitTextAsync"/>, the one writer. Returns the record and the extraction of the text.</summary>
    public async Task<(string RecordId, string TextExtractionId)> AdmitWithTextAsync(string json, string personId, string bodyText)
    {
        var document = FederalRegisterFixtures.Document(json);
        var configuration = FederalRegisterFixtures.Configuration(withTextSource: true);
        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var source = new ConfiguredCollectionSource(configuration, FederalRegisterFixtures.SourceId, today);
        var textSource = new ConfiguredCollectionSource(configuration, FederalRegisterFixtures.TextSourceId, today);
        var page = new FederalRegisterListPage([new FederalRegisterListItem(document.DocumentNumber, document.PublishedOn)]);
        var jobId = Assert.Single((await jobs.AdmitListPageAsync(source, page, default)).Jobs).JobId;
        await SucceededJobs.MarkSucceededAsync(connectionString, jobId);
        var text = FederalRegisterRenderer.Render(document);
        var extractionId = await extractions.SaveAsync(jobId, json, text, FederalRegisterDocumentParser.ExtractionVersion);
        var admitted = await jobs.AdmitDocumentAsync(source, jobId, new FederalRegisterEvidence(document, extractionId, text, personId), default);
        var textJobId = Assert.Single(admitted.Jobs).JobId;
        await SucceededJobs.MarkSucceededAsync(connectionString, textJobId);
        var reading = FederalRegisterRenderer.RenderText(document.DocumentNumber, bodyText);
        var textExtractionId = await extractions.SaveAsync(textJobId, FederalRegisterFixtures.OrderBodyHtml, reading.Text, "federal-register-text-v1+html-text-v1");
        await jobs.AdmitTextAsync(textSource, textJobId, new FederalRegisterTextEvidence(document.DocumentNumber, document.BodyHtmlUrl!, reading, textExtractionId), default);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT record_id FROM executive_action_drafts LIMIT 1", connection);
        return ((string)(await command.ExecuteScalarAsync())!, textExtractionId);
    }

    public async Task MakeLegacyAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            DELETE FROM executive_action_revision_disposition_targets;
            DELETE FROM executive_action_revision_facts;
            DELETE FROM executive_action_revision_notices;
            UPDATE executive_action_revisions SET source_id = NULL, document_number = NULL, kind = NULL, presidential_number = NULL, title = NULL,
                signed_on = NULL, published_on = NULL, federal_register_citation = NULL, president_name = NULL, person_id = NULL, document_url = NULL,
                disposition_notes = NULL, disposition_notes_extraction_id = NULL, disposition_notes_start = NULL, disposition_notes_length = NULL
            """, connection);
        await command.ExecuteNonQueryAsync();
    }
}
