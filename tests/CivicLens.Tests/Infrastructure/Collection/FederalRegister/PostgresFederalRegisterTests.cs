using CivicLens.Application.Collection;
using CivicLens.Application.Collection.FederalRegister;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.ExecutiveActions;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Documents;
using CivicLens.Tests.Fixtures;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.FederalRegister;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresFederalRegisterTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly string captureRoot = Path.Combine(Path.GetTempPath(), "federal-register-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PostgresCollectionJobStore store = null!;
    private JobExtractionSeeder extractions = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        store = PostgresCollectionJobStore.FromConnectionString(connectionString);
        var documents = PostgresDocumentExtractionStore.FromConnectionString(connectionString);
        Directory.CreateDirectory(captureRoot);
        var seeder = new StatementCandidateSeeder(PostgresCollectionAttemptStore.FromConnectionString(connectionString), documents,
            connectionString, captureRoot);
        extractions = new JobExtractionSeeder(store, documents, seeder, connectionString, captureRoot);
    }

    public async Task DisposeAsync()
    {
        await postgres.DropDatabaseAsync(connectionString);
        Directory.Delete(captureRoot, recursive: true);
    }

    [Fact]
    public async Task FirstDueCheckListsFromTheBackfillDateToTodayWithoutACredential()
    {
        var job = await SucceededJobs.ScheduledBillListAsync(store, connectionString, FederalRegisterFixtures.Configuration());

        Assert.Equal(ApiResource.FederalRegisterList, job.Definition.ApiResource);
        Assert.Equal(FederalRegisterUrl.List(FederalRegisterFixtures.SourceUrl,
            new FederalRegisterWindow(FederalRegisterFixtures.Backfill, DateOnly.FromDateTime(DateTime.UtcNow))), job.Definition.Url);
        Assert.Null(job.Definition.Credential);
    }

    [Fact]
    public async Task ListAdmitsOneDocumentJobForEachItemAndAReplayFindsThemKnown()
    {
        var page = FederalRegisterListParser.Parse(FederalRegisterFixtures.Bytes(FederalRegisterFixtures.List));

        var first = await store.AdmitListPageAsync(Source(), page, default);
        var replay = await store.AdmitListPageAsync(Source(), page, default);

        Assert.Equal(3, first.Jobs.Count);
        Assert.Contains(first.Jobs, job => job.Url == FederalRegisterUrl.Document(FederalRegisterFixtures.SourceUrl, "2025-01901"));
        Assert.Empty(replay.Jobs);
        Assert.Equal(3, replay.DuplicateCount);
    }

    [Fact]
    public async Task ADocumentWritesRevisionOneAndTheCodeApprovalInOneTransaction()
    {
        await AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump");

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM executive_action_revisions WHERE revision_number = 1"));
        Assert.Equal("Approved", await ScalarAsync("SELECT review_status FROM executive_action_drafts"));
        Assert.Equal(1, await ScalarAsync("SELECT review_state_version FROM executive_action_drafts"));
        Assert.Equal("civic-lens:code", await ScalarAsync("SELECT decision_json::jsonb ->> 'actorSubject' FROM executive_action_review_decisions"));
        Assert.Equal(new DateOnly(2025, 1, 20), await ScalarAsync("SELECT event_on FROM executive_action_drafts"));
        Assert.Equal("{donald-trump}", await ScalarAsync("SELECT official_ids::text FROM executive_action_drafts"));
        Assert.Equal(new DateOnly(2025, 1, 28), await ScalarAsync("SELECT published_on FROM executive_action_documents"));
    }

    [Fact]
    public async Task ADocumentThatIsKnownAddsNoSecondRevisionSoAReplayOfPreparationChangesNothing()
    {
        var job = await AdmitAsync(FederalRegisterFixtures.TrumpOrder, "donald-trump");

        var replay = await store.AdmitDocumentAsync(Source(), job.JobId, job.Evidence, default);

        Assert.Equal(1, replay.DuplicateCount);
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM executive_action_revisions"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM executive_action_review_decisions"));
    }

    [Fact]
    public async Task ACorrectionAppendsARevisionThatKeepsTheOriginalFactsAndAddsOneNoticeCitedToItsOwnCapture()
    {
        await AdmitAsync(FederalRegisterFixtures.BidenProclamation, "joe-biden");
        await AdmitAsync(FederalRegisterFixtures.BidenProclamationCorrection, "joe-biden");

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM executive_action_drafts"));
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM executive_action_revisions"));
        Assert.Equal(2, await ScalarAsync("SELECT current_revision_number FROM executive_action_drafts"));
        Assert.Equal("Approved", await ScalarAsync("SELECT review_status FROM executive_action_drafts"));
        Assert.Equal("2024-26714", await ScalarAsync("SELECT revision_json::jsonb ->> 'documentNumber' FROM executive_action_revisions WHERE revision_number = 2"));
        Assert.Equal("2024-11-13", await ScalarAsync("SELECT revision_json::jsonb ->> 'publishedOn' FROM executive_action_revisions WHERE revision_number = 2"));
        Assert.Equal("R1-2024-26714", await ScalarAsync("SELECT revision_json::jsonb -> 'notices' -> 0 ->> 'documentNumber' FROM executive_action_revisions WHERE revision_number = 2"));
        Assert.Equal(0, await ScalarAsync("SELECT jsonb_array_length(revision_json::jsonb -> 'notices') FROM executive_action_revisions WHERE revision_number = 1"));
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM executive_action_documents"));
    }

    [Fact]
    public async Task ACorrectionThatArrivesFirstWaitsAndAsksForTheOriginalThenTheOriginalStartsTheRecordWithTheNotice()
    {
        await AdmitAsync(FederalRegisterFixtures.BidenProclamationCorrection, "joe-biden");

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM executive_action_drafts"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM executive_action_documents WHERE revision_number IS NULL AND notice_json IS NOT NULL"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM collection_jobs WHERE idempotency_key LIKE '%/document/2024-26714'"));

        await AdmitAsync(FederalRegisterFixtures.BidenProclamation, "joe-biden");

        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM executive_action_drafts"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM executive_action_revisions"));
        Assert.Equal("Approved", await ScalarAsync("SELECT review_status FROM executive_action_drafts"));
        Assert.Equal("2024-26714", await ScalarAsync("SELECT revision_json::jsonb ->> 'documentNumber' FROM executive_action_revisions"));
        Assert.Equal("2024-11-13", await ScalarAsync("SELECT revision_json::jsonb ->> 'publishedOn' FROM executive_action_revisions"));
        Assert.Equal("R1-2024-26714", await ScalarAsync("SELECT revision_json::jsonb -> 'notices' -> 0 ->> 'documentNumber' FROM executive_action_revisions"));
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM executive_action_documents WHERE revision_number = 1"));
    }

    [Fact]
    public async Task AnExtractionOfAnotherJobIsInvalidDataAndRecordsNothing()
    {
        var job = await EnqueueDocumentAsync("2025-01900");
        var other = await EnqueueDocumentAsync("2025-01901");
        var foreign = await extractions.SaveAsync(other, "x", "x", "extraction");
        var document = FederalRegisterFixtures.Document(FederalRegisterFixtures.TrumpOrder);

        await Assert.ThrowsAsync<InvalidDataException>(() => store.AdmitDocumentAsync(Source(), job,
            new FederalRegisterEvidence(document, foreign, FederalRegisterRenderer.Render(document), "donald-trump"), default));

        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM executive_action_drafts"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM executive_action_documents"));
    }

    [Fact]
    public async Task TheNextWindowStartsTwoDaysBeforeTheNewestKnownPublicationDate()
    {
        var root = await SucceededJobs.ScheduledBillListAsync(store, connectionString, FederalRegisterFixtures.Configuration());
        await SucceededJobs.MarkSucceededAsync(connectionString, root.JobId);
        await AdmitAsync(FederalRegisterFixtures.BidenProclamation, "joe-biden");
        await ExecuteAsync("UPDATE collection_source_schedules SET next_due_utc_ticks = 1");

        Assert.Equal(1, await store.AdvanceDueAsync(10, default));

        var next = (await store.ListAsync(10, default)).Single(job => job.JobId != root.JobId && job.Definition.ApiResource == ApiResource.FederalRegisterList);
        Assert.Equal(FederalRegisterUrl.List(FederalRegisterFixtures.SourceUrl,
            new FederalRegisterWindow(new DateOnly(2024, 11, 11), DateOnly.FromDateTime(DateTime.UtcNow))), next.Definition.Url);
    }

    private sealed record AdmittedDocument(string JobId, FederalRegisterEvidence Evidence);

    private async Task<string> EnqueueDocumentAsync(string number)
    {
        var page = new FederalRegisterListPage([new FederalRegisterListItem(number, new DateOnly(2025, 1, 28))]);
        _ = await store.AdmitListPageAsync(Source(), page, default);
        var jobId = (string)(await ScalarAsync($"SELECT job_id FROM collection_jobs WHERE idempotency_key LIKE '%/document/{number}'"))!;
        await SucceededJobs.MarkSucceededAsync(connectionString, jobId);
        return jobId;
    }

    private async Task<AdmittedDocument> AdmitAsync(string json, string personId)
    {
        var document = FederalRegisterFixtures.Document(json);
        var jobId = await EnqueueDocumentAsync(document.DocumentNumber);
        var text = FederalRegisterRenderer.Render(document);
        var extractionId = await extractions.SaveAsync(jobId, json, text, FederalRegisterDocumentParser.ExtractionVersion);
        var evidence = new FederalRegisterEvidence(document, extractionId, text, personId);
        await store.AdmitDocumentAsync(Source(), jobId, evidence, default);
        return new(jobId, evidence);
    }

    private static ConfiguredCollectionSource Source() =>
        new(FederalRegisterFixtures.Configuration(), FederalRegisterFixtures.SourceId, DateOnly.FromDateTime(DateTime.UtcNow));

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
