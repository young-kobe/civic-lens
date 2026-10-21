using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Bills;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Documents;
using CivicLens.Application.Issues;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Core.Registry;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Collection.Processing;
using CivicLens.Infrastructure.Documents;
using CivicLens.Infrastructure.Roster;
using CivicLens.Tests.Application.Collection.Bills;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using static CivicLens.Tests.Fixtures.CollectionFixtures;

namespace CivicLens.Tests.Infrastructure.Collection.Processing;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class EvidenceProcessingWorkerTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly string artifactRoot = Path.Combine(Path.GetTempPath(), "civic-processing-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PostgresCollectionJobStore jobs = null!;
    private PostgresCollectionAttemptStore attempts = null!;
    private PostgresEvidenceProcessingStore processing = null!;
    private PostgresDocumentExtractionStore extractions = null!;
    private PostgresDocumentComparisonStore comparisons = null!;
    private ExtractDocument extract = null!;
    private IssueConfigurationRevision? issues = BillGateFixtures.Revision();

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        jobs = PostgresCollectionJobStore.FromConnectionString(connectionString);
        attempts = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        processing = PostgresEvidenceProcessingStore.FromConnectionString(connectionString);
        extractions = PostgresDocumentExtractionStore.FromConnectionString(connectionString);
        comparisons = PostgresDocumentComparisonStore.FromConnectionString(connectionString);
        extract = new ExtractDocument(attempts, new CaptureDocumentTextExtractor(), extractions);
        Directory.CreateDirectory(artifactRoot);
    }

    public async Task DisposeAsync()
    {
        if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        await postgres.DropDatabaseAsync(connectionString);
    }

    [Fact]
    public async Task UpgradeRegistersLegacySuccessfulJobsAndResumesTheirPipeline()
    {
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new CollectionAttemptDbContext(options);
        await db.GetService<IMigrator>().MigrateAsync("20261008190000_DurableEvidenceProcessing");
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE collection_jobs ADD COLUMN configuration_revision_id text, ADD COLUMN pacing_key text, " +
            "ADD COLUMN source_id text, ADD COLUMN is_root boolean");
        var beforeJob = await CollectAsync("earlier", "Earlier wording.", legacySchema: true);
        var afterJob = await CollectAsync("later", "Later wording.", legacySchema: true);
        await db.Database.ExecuteSqlRawAsync("ALTER TABLE collection_jobs DROP COLUMN configuration_revision_id, DROP COLUMN pacing_key, " +
            "DROP COLUMN source_id, DROP COLUMN is_root");
        var beforeAttempt = Assert.Single(beforeJob.Attempts).AttemptId;
        var afterAttempt = Assert.Single(afterJob.Attempts).AttemptId;

        await attempts.MigrateAsync();
        var recovered = await processing.GetByJobIdsAsync([beforeJob.JobId, afterJob.JobId], default);
        Assert.Equal(2, recovered.Count);
        Assert.All(recovered, record => Assert.Equal("prepare", record.AttemptId));
        var completed = await ProcessUntilTerminalAsync(Worker(processing), afterJob.JobId, afterAttempt);
        Assert.Equal(EvidenceProcessingOutcome.Changed, completed.Outcome);
        var beforeRecord = await RecordAsync(beforeJob.JobId, beforeAttempt);
        var comparison = await comparisons.GetAsync(completed.ComparisonId!, default);
        Assert.Equal(beforeRecord!.ExtractionId, comparison!.BeforeExtractionId);
        Assert.Equal(completed.ExtractionId, comparison.AfterExtractionId);
        Assert.NotEmpty(comparison.Hunks);
    }

    [Fact]
    public async Task ManualEarlierExtractionWithoutManagedJobIsComparedDirectly()
    {
        var manualAttemptId = "manual-" + Guid.NewGuid().ToString("N");
        var manualRequest = ConfiguredDefinition().CreateRequest("manual-job", artifactRoot);
        var manualReceipt = await CapturedReceiptAsync(manualRequest, "Earlier manual wording.", ObservationEpoch.AddMinutes(-2));
        var imported = await attempts.ImportAtomicallyAsync(CollectionAttemptImporter.CreateImport(
            manualAttemptId, manualRequest, manualReceipt), default);
        Assert.IsType<CapturedAttemptResult>(imported.AttemptResult);
        var profile = Profile().ToProfile();
        var manualExtraction = await extract.ExecuteAsync(manualAttemptId, artifactRoot, profile);

        var currentJob = await CollectAsync("manual-current", "Later manual wording.");
        var currentAttempt = Assert.Single(currentJob.Attempts).AttemptId;
        var completed = await ProcessUntilTerminalAsync(Worker(processing), currentJob.JobId, currentAttempt);

        Assert.Equal(EvidenceProcessingOutcome.Changed, completed.Outcome);
        var comparison = await comparisons.GetAsync(completed.ComparisonId!, default);
        Assert.Equal(manualExtraction.ExtractionId, comparison!.BeforeExtractionId);
        Assert.NotEmpty(comparison.Hunks);
    }

    [Fact]
    public async Task OnceWorkerDrainsCollectionThroughPreparationExtractionAndComparison()
    {
        var definition = ConfiguredDefinition();
        var job = await jobs.EnqueueAsync(definition, "pipeline-once", default);
        var runner = new RunCollectionJob(jobs, attempts, new FileCollectionReceiptHandoffStore(artifactRoot),
            new CaptureArtifactVerifier(), new FixtureCollector("Pipeline wording.", fail: false,
                Interlocked.Increment(ref observationTick)));
        var pipeline = new CollectionJobWorker(PostgresCollectionWorkerQueue.FromConnectionString(connectionString),
            runner, Worker(processing));

        var result = await pipeline.ExecuteAsync(artifactRoot, once: true, batchSize: 20,
            leaseDuration: TimeSpan.FromSeconds(60));

        Assert.Equal(1, result.JobsVisited);
        var completedJob = await jobs.GetAsync(job.JobId, default);
        Assert.Equal(CollectionJobState.Succeeded, completedJob!.State);
        var attemptId = Assert.Single(completedJob.Attempts).AttemptId;
        var record = await RecordAsync(job.JobId, attemptId);
        Assert.Equal(EvidenceProcessingStatus.Succeeded, record.Status);
        Assert.Equal(EvidenceProcessingOutcome.Baseline, record.Outcome);
        Assert.NotNull(await extractions.GetAsync(record.ExtractionId!, default));
    }

    [Fact]
    public async Task ApiCaptureWithoutAResourceIsBlockedInsteadOfSilentlyPrepared()
    {
        var rosterJob = new ConfiguredCollectionSource(RosterFixtures.Configuration(), RosterFixtures.SourceId)
            .CreateJobDefinition(DateOnly.FromDateTime(ObservationEpoch.UtcDateTime));
        var definition = rosterJob with { ApiResource = null };
        var job = await jobs.EnqueueAsync(definition, "api-once", default);
        var runner = new RunCollectionJob(jobs, attempts, new FileCollectionReceiptHandoffStore(artifactRoot),
            new CaptureArtifactVerifier(), new ApiFixtureCollector(RosterFixtures.Json));
        var pipeline = new CollectionJobWorker(PostgresCollectionWorkerQueue.FromConnectionString(connectionString),
            runner, Worker(processing));

        _ = await pipeline.ExecuteAsync(artifactRoot, once: true, batchSize: 20, leaseDuration: TimeSpan.FromSeconds(60));

        var completedJob = await jobs.GetAsync(job.JobId, default);
        Assert.Equal(CollectionJobState.Succeeded, completedJob!.State);
        var record = Assert.Single(await processing.GetByJobIdsAsync([job.JobId], default));
        Assert.Equal("prepare", record.AttemptId);
        Assert.Equal(EvidenceProcessingStatus.Blocked, record.Status);
        Assert.Equal("apiResourceMissing", record.ErrorCode);
    }

    [Fact]
    public async Task AScheduledRosterJobRunsThroughTheWorkerAndWritesRosterRows()
    {
        await jobs.SynchronizeAsync(RosterFixtures.Configuration(), default);
        await MakeEverySourceDueAsync();
        Assert.Equal(1, await jobs.AdvanceDueAsync(10, default));
        var runner = new RunCollectionJob(jobs, attempts, new FileCollectionReceiptHandoffStore(artifactRoot),
            new CaptureArtifactVerifier(), new ApiFixtureCollector(RosterFixtures.Json));
        var pipeline = new CollectionJobWorker(PostgresCollectionWorkerQueue.FromConnectionString(connectionString),
            runner, Worker(processing));

        _ = await pipeline.ExecuteAsync(artifactRoot, once: true, batchSize: 20, leaseDuration: TimeSpan.FromSeconds(60));

        var job = Assert.Single(await jobs.ListAsync(10, default));
        Assert.Equal(CollectionJobState.Succeeded, job.State);
        var record = Assert.Single(await processing.GetByJobIdsAsync([job.JobId], default));
        Assert.Equal(EvidenceProcessingStatus.Succeeded, record.Status);
        Assert.Equal(EvidenceProcessingStage.Complete, record.Stage);
        Assert.Null(record.ExtractionId);
        var members = await PostgresMemberRosterStore.FromConnectionString(connectionString)
            .ReadLatestAsync(RosterFixtures.SourceId, default);
        Assert.Equal(RosterFixtures.Members().Length, members.Count);
    }

    private async Task MakeEverySourceDueAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE collection_source_schedules SET next_due_utc_ticks = 0", connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task BillListCaptureAdmitsItsBillJobsAndKeepsTheCursorUntilAPassAdmitsNoNewBills()
    {
        var job = await RunBillJobAsync(ApiResource.BillList, BillWindow.RequestUrl(BillSourceUrl), """{"bills":[{"congress":119,"type":"HR","number":"1","updateDate":"2025-01-03"},{"congress":118,"type":"HR","number":"2","updateDate":"2025-01-03"}],"pagination":{"count":2}}""");

        var record = Assert.Single(await processing.GetByJobIdsAsync([job.JobId], default));
        Assert.Equal(EvidenceProcessingStatus.Succeeded, record.Status);
        Assert.Equal(1, record.AdmittedCount);
        var detail = Assert.Single(await jobs.ListAsync(10, default), item => item.JobId != job.JobId);
        Assert.Equal("https://api.congress.gov/v3/bill/119/hr/1?format=json", detail.Definition.Url);
        Assert.Equal(ApiResource.Bill, detail.Definition.ApiResource);
        Assert.Null(await ReadBillCursorAsync());
    }

    [Fact]
    public async Task BillDetailCaptureStoresItsGateAndAdmitsLinkedFollowOnPagesWithoutAProcessingStage()
    {
        var job = await RunBillJobAsync(ApiResource.Bill, BillSourceUrl + "/119/hr/1?format=json", BillDetail(sponsor: "A000001", cosponsors: 2));

        var record = Assert.Single(await processing.GetByJobIdsAsync([job.JobId], default));
        Assert.Equal(EvidenceProcessingStatus.Succeeded, record.Status);
        Assert.Equal(EvidenceProcessingStage.Complete, record.Stage);
        Assert.Equal(2, record.AdmittedCount);
        Assert.Equal(("Passed", "CoveredSponsor"), await ReadGateAsync());
        Assert.Equal(2L, await ScalarAsync("SELECT count(*) FROM bill_follow_on_jobs"));
        Assert.Null(await ReadBillCursorAsync());
    }

    [Fact]
    public async Task BillDetailCaptureWithoutAnIssuesConfigurationBlocksAndStoresNoGate()
    {
        issues = null;

        var job = await RunBillJobAsync(ApiResource.Bill, BillSourceUrl + "/119/hr/1?format=json", BillDetail(sponsor: "A000001", cosponsors: 2));

        var record = Assert.Single(await processing.GetByJobIdsAsync([job.JobId], default));
        Assert.Equal(EvidenceProcessingStatus.Blocked, record.Status);
        Assert.Equal("issuesConfigurationUnavailable", record.ErrorCode);
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM bill_gates"));
        Assert.Single(await jobs.ListAsync(10, default));
    }

    [Fact]
    public async Task EveryBillPageCaptureGetsOneExtractionAndNeverAStatementCandidate()
    {
        var detail = await RunBillJobAsync(ApiResource.Bill, BillSourceUrl + "/119/hr/1?format=json", BillDetail(sponsor: "A000001", cosponsors: 2));
        var attempt = Assert.Single(detail.Attempts).AttemptId;

        var saved = Assert.Single(await ReadBillExtractionsAsync());
        Assert.Equal((attempt, "congress-bill-v1", "congress-bill-render-v1"), (saved.AttemptId, saved.ParserVersion, saved.NormalizationVersion));
        Assert.StartsWith("Bill: 119 hr 1\nTitle: A bill\n", saved.Text);
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM statement_candidates"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM statement_analysis"));
    }

    [Fact]
    public async Task PreparingABillDetailAgainKeepsTheOneExtractionEvenThoughTheGateIsADuplicate()
    {
        var detail = await RunBillJobAsync(ApiResource.Bill, BillSourceUrl + "/119/hr/1?format=json", BillDetail(sponsor: "A000001", cosponsors: 2));
        var replay = new JobPreparation(jobs, attempts, jobs, jobs, jobs, PostgresMemberRosterStore.FromConnectionString(connectionString), PostgresExecutiveRosterStore.FromConnectionString(connectionString), new CaptureBodyReader(), processing, extractions, new CaptureDocumentTextExtractor(), issues);

        var result = await replay.PrepareAsync(detail.JobId, artifactRoot, default);

        Assert.Equal(0, result!.AdmittedCount);
        Assert.Single(await ReadBillExtractionsAsync());
    }

    [Fact]
    public async Task ADetailWhoseTitleHoldsANulBlocksAndWritesNoExtractionGateOrFollowOnJob()
    {
        var job = await RunBillJobAsync(ApiResource.Bill, BillSourceUrl + "/119/hr/1?format=json",
            BillDetail(sponsor: "A000001", cosponsors: 2).Replace("A bill", "A\\u0000 bill"));

        var record = Assert.Single(await processing.GetByJobIdsAsync([job.JobId], default));
        Assert.Equal(EvidenceProcessingStatus.Blocked, record.Status);
        Assert.Empty(await ReadBillExtractionsAsync());
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM bill_gates"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM bill_follow_on_jobs"));
    }

    [Fact]
    public async Task ANotRelevantBillIsStillExtracted()
    {
        _ = await RunBillJobAsync(ApiResource.Bill, BillSourceUrl + "/119/hr/1?format=json", BillDetail(sponsor: "Z000009", cosponsors: 0, policyArea: "Zoology"));

        Assert.Equal(("NotRelevant", "UntrackedPolicyArea"), await ReadGateAsync());
        Assert.Single(await ReadBillExtractionsAsync());
    }

    [Fact]
    public async Task CosponsorAndActionsPagesAreExtractedByTheirOwnParsers()
    {
        _ = await RunBillJobAsync(ApiResource.BillCosponsors, BillSourceUrl + "/119/hr/1/cosponsors?format=json&limit=250",
            """{"cosponsors":[{"bioguideId":"A000001","sponsorshipDate":"2025-01-04","isOriginalCosponsor":true}],"pagination":{}}""");

        var saved = Assert.Single(await ReadBillExtractionsAsync());
        Assert.Equal(("congress-bill-cosponsors-v1", "Bill: 119 hr 1\nCosponsor: A000001, original, 2025-01-04"),
            (saved.ParserVersion, saved.Text));
    }

    [Fact]
    public async Task ASummariesPageIsExtractedWithTheCombinedParserVersionAndNeverBecomesAStatementCandidate()
    {
        _ = await RunBillJobAsync(ApiResource.BillSummaries, BillSourceUrl + "/119/hr/1/summaries?format=json&limit=1",
            """{"summaries":[{"versionCode":"173","actionDate":"2025-01-04","actionDesc":"Introduced in House","updateDate":"2025-01-05T08:00:00Z","text":"<p><strong>Title.</strong></p><p>Requires the agency to act.</p>"}],"pagination":{}}""");

        var saved = Assert.Single(await ReadBillExtractionsAsync());
        Assert.Equal(("congress-bill-summaries-v1+capture-text-v2+body-text-v1", "congress-bill-render-v1"),
            (saved.ParserVersion, saved.NormalizationVersion));
        Assert.Equal("Bill: 119 hr 1\n\nSummary version: 173\nAction date: 2025-01-04\nAction: Introduced in House\n" +
            "Updated: 2025-01-05T08:00:00Z\nText:\nTitle.\nRequires the agency to act.", saved.Text);
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM statement_candidates"));
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM statement_analysis"));
    }

    [Fact]
    public async Task ABillDetailWithSummariesAdmitsItsSummariesPageBesideTheOthers()
    {
        _ = await RunBillJobAsync(ApiResource.Bill, BillSourceUrl + "/119/hr/1?format=json", BillDetail(sponsor: "A000001", cosponsors: 2, summaries: 3));

        Assert.Equal(3L, await ScalarAsync("SELECT count(*) FROM bill_follow_on_jobs"));
        Assert.Equal(1L, await ScalarAsync("SELECT count(*) FROM collection_jobs WHERE idempotency_key LIKE '%/summaries/%'"));
    }

    [Theory]
    [InlineData(ApiResource.Bill, "/119/hr/1?format=json")]
    [InlineData(ApiResource.BillCosponsors, "/119/hr/1/cosponsors?format=json&limit=250")]
    [InlineData(ApiResource.BillActions, "/119/hr/1/actions?format=json&limit=250")]
    [InlineData(ApiResource.BillSummaries, "/119/hr/1/summaries?format=json&limit=1")]
    public async Task MalformedBillResponsesBlockPreparationLikeABadListPage(ApiResource resource, string path)
    {
        var job = await RunBillJobAsync(resource, BillSourceUrl + path, """{"unexpected":true}""");

        var record = Assert.Single(await processing.GetByJobIdsAsync([job.JobId], default));
        Assert.Equal(EvidenceProcessingStatus.Blocked, record.Status);
        Assert.Equal(0L, await ScalarAsync("SELECT count(*) FROM bill_gates"));
        Assert.Single(await jobs.ListAsync(10, default));
    }

    [Fact]
    public async Task MalformedBillListCaptureBlocksPreparationAndLeavesTheCursorBehind()
    {
        var job = await RunBillJobAsync(ApiResource.BillList, BillWindow.RequestUrl(BillSourceUrl), """{"bills":[{"congress":119,"type":"HR","number":"1"}],"pagination":{}}""");

        var record = Assert.Single(await processing.GetByJobIdsAsync([job.JobId], default));
        Assert.Equal(EvidenceProcessingStatus.Blocked, record.Status);
        Assert.Single(await jobs.ListAsync(10, default));
        Assert.Null(await ReadBillCursorAsync());
    }

    [Fact]
    public async Task ComparisonReloadsHistoryAfterPredecessorReportsReady()
    {
        var beforeJob = await CollectAsync("fresh-before", "Earlier wording.");
        var beforeAttempt = Assert.Single(beforeJob.Attempts).AttemptId;
        _ = await processing.EnsureAsync(beforeJob.JobId, beforeAttempt, "source", Url, default);
        var before = await ProcessUntilTerminalAsync(Worker(processing), beforeJob.JobId, beforeAttempt);
        Assert.Equal(EvidenceProcessingOutcome.Baseline, before.Outcome);

        var afterJob = await CollectAsync("fresh-after", "Later wording.");
        var afterAttempt = Assert.Single(afterJob.Attempts).AttemptId;
        _ = await processing.EnsureAsync(afterJob.JobId, afterAttempt, "source", Url, default);
        var staleHistory = new StalePredecessorHistoryStore(
            PostgresDocumentHistoryStore.FromConnectionString(connectionString), beforeAttempt);
        var worker = Worker(processing, staleHistory);
        _ = await worker.ExecuteAsync(artifactRoot, 20, TimeSpan.FromSeconds(60), default);
        var completed = await ProcessUntilTerminalAsync(worker, afterJob.JobId, afterAttempt);

        Assert.Equal(EvidenceProcessingOutcome.Changed, completed.Outcome);
        Assert.True(staleHistory.ReadCount >= 2);
        var comparison = await comparisons.GetAsync(completed.ComparisonId!, default);
        Assert.Equal(before.ExtractionId, comparison!.BeforeExtractionId);
        Assert.NotEmpty(comparison.Hunks);
    }

    [Fact]
    public async Task OneSecondLeaseIsRenewedWhileComparisonWorkIsStillBlocked()
    {
        var job = await CollectAsync("short-lease", "Short lease wording.");
        var attemptId = Assert.Single(job.Attempts).AttemptId;
        var worker = Worker(processing);
        _ = await worker.ExecuteAsync(artifactRoot, 20, TimeSpan.FromSeconds(60), default);
        _ = await worker.ExecuteAsync(artifactRoot, 20, TimeSpan.FromSeconds(60), default);
        var record = await RecordAsync(job.JobId, attemptId);
        Assert.Equal(EvidenceProcessingStage.Comparison, record.Stage);
        Assert.Equal(EvidenceProcessingStatus.Pending, record.Status);

        var historyGate = new GatedDocumentHistoryStore(
            PostgresDocumentHistoryStore.FromConnectionString(connectionString));
        var observedProcessing = new ObservedEvidenceProcessingStore(processing);
        var blockedWorker = Worker(observedProcessing, historyGate);
        var execution = blockedWorker.ExecuteAsync(artifactRoot, 20, TimeSpan.FromSeconds(1), default);
        try
        {
            await historyGate.Entered.Task.WaitAsync(TimeSpan.FromSeconds(5));
            var renewed = await observedProcessing.Renewed.Task.WaitAsync(TimeSpan.FromSeconds(5));

            // The store only reports success while the database lease is still live.
            Assert.True(renewed);
        }
        finally
        {
            historyGate.Release.TrySetResult();
        }

        await execution.WaitAsync(TimeSpan.FromSeconds(5));
        Assert.Equal(EvidenceProcessingStatus.Succeeded,
            (await RecordAsync(job.JobId, attemptId)).Status);
    }

    [Fact]
    public async Task InvalidLeaseDurationIsRejectedBeforeReadingEligibleWork()
    {
        var observedProcessing = new ObservedEvidenceProcessingStore(processing);
        var worker = Worker(observedProcessing);

        await Assert.ThrowsAsync<ArgumentOutOfRangeException>(() =>
            worker.ExecuteAsync(artifactRoot, 20, TimeSpan.Zero, default));

        Assert.Equal(0, observedProcessing.EligibleReads);
    }

    [Fact]
    public async Task ResolvedNotModifiedObservationUsesItsLinkedCaptureAndFailedObservationStartsHistoryGap()
    {
        var captured = await CollectAsync("before-304", "Before conditional check.");
        var priorAttempt = Assert.Single(captured.Attempts).AttemptId;
        await ProcessUntilTerminalAsync(Worker(processing), captured.JobId, priorAttempt);
        var conditionalJob = await CollectAsync("not-modified", body: null, etag: "\"v1\"");
        var conditionalAttempt = Assert.Single(conditionalJob.Attempts).AttemptId;
        var notModified = await attempts.GetAsync(conditionalAttempt, default);
        Assert.Equal(priorAttempt, notModified!.PriorCapturedAttempt!.AttemptId);
        var after304Job = await CollectAsync("after-304", "After conditional check.");
        var after304Attempt = Assert.Single(after304Job.Attempts).AttemptId;
        var after304 = await ProcessUntilTerminalAsync(Worker(processing), after304Job.JobId, after304Attempt);
        Assert.Equal(EvidenceProcessingOutcome.Changed, after304.Outcome);
        var originalRecord = await RecordAsync(captured.JobId, priorAttempt);
        var originalExtraction = await extractions.GetAsync(originalRecord!.ExtractionId!, default);
        var after304Comparison = await comparisons.GetAsync(after304.ComparisonId!, default);
        Assert.Equal(originalExtraction!.ExtractionId, after304Comparison!.BeforeExtractionId);
        Assert.NotEmpty(after304Comparison.Hunks);

        var earlierCapture = await CollectAsync("before-failure", "Text before failed observation.");
        await ProcessUntilTerminalAsync(Worker(processing), earlierCapture.JobId, Assert.Single(earlierCapture.Attempts).AttemptId);
        var failedJob = await CollectAsync("failed-observation", body: null, fail: true);
        Assert.NotEqual(CollectionJobState.Succeeded, failedJob.State);
        var resumedJob = await CollectAsync("after-failure", "Text after failed observation.");
        var resumedAttempt = Assert.Single(resumedJob.Attempts).AttemptId;
        var resumed = await ProcessUntilTerminalAsync(Worker(processing), resumedJob.JobId, resumedAttempt);
        Assert.Equal(EvidenceProcessingOutcome.Baseline, resumed.Outcome);
        Assert.Equal("historyGap", resumed.ErrorCode);
    }

    private async Task<CollectionJobRecord> CollectAsync(string key, string? body, string? etag = null, bool fail = false,
        bool legacySchema = false)
    {
        var configured = ConfiguredDefinition(etag);
        var definition = legacySchema ? configured with { ConfigurationRevision = null, CoverageAsOf = null } : configured;
        var job = await jobs.EnqueueAsync(definition, key, default);
        var collector = new FixtureCollector(body, fail, Interlocked.Increment(ref observationTick));
        var runner = new RunCollectionJob(jobs, attempts, new FileCollectionReceiptHandoffStore(artifactRoot),
            new CaptureArtifactVerifier(), collector);
        var result = await runner.ExecuteAsync(job.JobId, artifactRoot, TimeSpan.FromSeconds(60), default);
        Assert.True(result.Status is CollectionJobRunStatus.Completed or CollectionJobRunStatus.AttemptFailed,
            $"Collection returned {result.Status}: {result.BlockReason}, retry {result.RetryAt:O}.");
        var record = (await jobs.GetAsync(job.JobId, default))!;
        if (legacySchema) await StoreLegacyDefinitionAsync(job.JobId, configured);
        return record;
    }

    private async Task StoreLegacyDefinitionAsync(string jobId, CollectionJobDefinition definition)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("UPDATE collection_jobs SET definition_json = @definition WHERE job_id = @id", connection);
        command.Parameters.AddWithValue("definition", JsonSerializer.Serialize(definition, CollectionProtocol.JsonOptions));
        command.Parameters.AddWithValue("id", jobId);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<EvidenceProcessingRecord> ProcessUntilTerminalAsync(EvidenceProcessingWorker worker,
        string jobId, string attemptId)
    {
        for (var pass = 0; pass < 50; pass++)
        {
            _ = await worker.ExecuteAsync(artifactRoot, 20, TimeSpan.FromSeconds(60), default);
            var record = (await processing.GetByJobIdsAsync([jobId], default))
                .SingleOrDefault(candidate => candidate.AttemptId == attemptId);
            if (record?.Status is EvidenceProcessingStatus.Succeeded or EvidenceProcessingStatus.Blocked or EvidenceProcessingStatus.Failed)
                return record;
            if (record?.Status is EvidenceProcessingStatus.RetryWaiting)
                throw new InvalidOperationException(
                    $"Processing attempt '{attemptId}' hit a transient error at {record.Stage} after {record.Attempts} attempts: {record.ErrorCode}.");
        }
        throw new InvalidOperationException($"Processing attempt '{attemptId}' did not reach a terminal state.");
    }

    private async Task<EvidenceProcessingRecord> RecordAsync(string jobId, string attemptId) =>
        Assert.Single(await processing.GetByJobIdsAsync([jobId], default), record => record.AttemptId == attemptId);

    private EvidenceProcessingWorker Worker(IEvidenceProcessingStore processingStore, IDocumentHistoryStore? historyStore = null) => new(jobs,
        new JobPreparation(jobs, attempts, jobs, jobs, jobs, PostgresMemberRosterStore.FromConnectionString(connectionString), PostgresExecutiveRosterStore.FromConnectionString(connectionString), new CaptureBodyReader(), processingStore, extractions, new CaptureDocumentTextExtractor(), issues), processingStore, extract, new GetDocumentHistory(historyStore ?? PostgresDocumentHistoryStore.FromConnectionString(connectionString)),
        new CompareDocuments(comparisons), 1);

    private CollectionJobDefinition ConfiguredDefinition(string? etag = null)
    {
        var source = new ConfiguredCollectionSource(Configuration(etag), "source");
        return source.CreateJobDefinition(DateOnly.FromDateTime(ObservationEpoch.UtcDateTime));
    }

    private static CollectionConfiguration Configuration(string? etag = null) => new()
    {
        Version = 2,
        People = [new PersonConfiguration { Id = "person", Name = "Test Person" }],
        DocumentProfiles = [Profile()],
        Sources = [new WatchedSourceConfiguration
        {
            Id = "source", Coverage = [new SourceCoverageConfiguration { PersonId = "person" }],
            Url = Url, AllowedOrigin = "https://example.test", AllowedPathPrefix = "/pages",
            DocumentProfileId = "main", ETag = etag, MaxRequests = 10, MaxBytes = 100_000,
            TimeoutSeconds = 10, MinDelayMilliseconds = 0,
            JobPolicy = new CollectionJobPolicy { MaxAttempts = 1, MaxTotalRequests = 10,
                MaxTotalBytes = 100_000, MaxTotalTimeoutSeconds = 10, InitialRetryDelaySeconds = 0 }
        }]
    };

    private static DocumentProfileConfiguration Profile() => new() { Id = "main", Selector = "main" };

    private async Task<CollectionResult> CapturedReceiptAsync(CollectionRequest request, string text, DateTimeOffset observedAt)
    {
        var html = Encoding.UTF8.GetBytes($"<html><body><main>{text}</main></body></html>");
        var hash = Convert.ToHexStringLower(SHA256.HashData(html));
        await using (var file = File.Create(Path.Combine(request.ArtifactDirectory, hash + ".gz")))
        await using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
            await gzip.WriteAsync(html);
        return Receipt(request) with
        {
            ObservedAt = observedAt,
            SentValidators = request.ETag is null && request.LastModified is null ? null :
                new HttpRequestValidators { ETag = request.ETag, LastModified = request.LastModified },
            Response = new HttpResponseMetadata { StatusCode = 200, ETag = "\"v1\"", ContentType = "text/html; charset=utf-8", ContentEncodings = [] },
            Capture = new CaptureArtifact { Sha256 = hash, RelativePath = hash + ".gz", ByteLength = html.Length },
            BytesReceived = html.Length
        };
    }

    private const string Url = "https://example.test/pages/a";
    private static readonly DateTimeOffset ObservationEpoch = new(2026, 6, 1, 12, 0, 0, TimeSpan.Zero);
    private static long observationTick;

    private sealed class FixtureCollector(string? body, bool fail, long sequence) : ICollectorProcess
    {
        public async Task<CollectionResult> RunAsync(CollectionRequest request, CancellationToken cancellationToken)
        {
            var observedAt = ObservationEpoch.AddTicks(sequence);
            if (fail)
                return Receipt(request) with
                {
                    Outcome = CollectionOutcome.Failed,
                    ObservedAt = observedAt,
                    Response = null,
                    Capture = null,
                    BytesReceived = 0,
                    FailureCode = CollectionFailureCode.TransportError
                };
            if (body is null)
                return Receipt(request) with
                {
                    Outcome = CollectionOutcome.NotModified,
                    ObservedAt = observedAt,
                    Response = new HttpResponseMetadata { StatusCode = 304, ETag = request.ETag, ContentEncodings = [] },
                    SentValidators = new HttpRequestValidators { ETag = request.ETag, LastModified = request.LastModified },
                    Capture = null,
                    BytesReceived = 0
                };
            return await new FixtureCollectorWriter().WriteAsync(request, body, observedAt, cancellationToken);
        }
    }

    private static readonly BillStreamWindow BillWindow = new(new DateOnly(2025, 1, 3), new DateOnly(2025, 1, 4));

    private const string BillSourceUrl = "https://api.congress.gov/v3/bill";

    private async Task<CollectionJobRecord> RunBillJobAsync(ApiResource resource, string url, string json)
    {
        var today = DateOnly.FromDateTime(ObservationEpoch.UtcDateTime);
        var definition = new ConfiguredCollectionSource(CongressBillSourceConfigurationTests.Configuration(), "congress-bills", today)
            .CreateApiJobDefinition(resource, url, today);
        var job = await jobs.EnqueueAsync(definition, "bill-list-once", default);
        var runner = new RunCollectionJob(jobs, attempts, new FileCollectionReceiptHandoffStore(artifactRoot),
            new CaptureArtifactVerifier(), new ApiFixtureCollector(json));
        var pipeline = new CollectionJobWorker(PostgresCollectionWorkerQueue.FromConnectionString(connectionString),
            runner, Worker(processing));
        _ = await pipeline.ExecuteAsync(artifactRoot, once: true, batchSize: 20, leaseDuration: TimeSpan.FromSeconds(60));
        return (await jobs.GetAsync(job.JobId, default))!;
    }

    private static string BillDetail(string sponsor, int cosponsors, string policyArea = "Taxation", int summaries = 0) => $$$"""
        {"bill":{"congress":119,"type":"HR","number":"1","title":"A bill","introducedDate":"2025-01-03",
         "updateDate":"2025-01-03T12:00:00Z","policyArea":{"name":"{{{policyArea}}}"},"sponsors":[{"bioguideId":"{{{sponsor}}}"}],
         "cosponsors":{"countIncludingWithdrawnCosponsors":{{{cosponsors}}} },"actions":{"count":1},"summaries":{"count":{{{summaries}}} } } }
        """;

    private async Task<List<(string AttemptId, string ParserVersion, string NormalizationVersion, string Text)>> ReadBillExtractionsAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT attempt_id, parser_version, normalization_version, text FROM document_extractions ORDER BY extraction_id", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<(string, string, string, string)>();
        while (await reader.ReadAsync()) rows.Add((reader.GetString(0), reader.GetString(1), reader.GetString(2), reader.GetString(3)));
        return rows;
    }

    private async Task<(string Decision, string? Reason)> ReadGateAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT decision, reason FROM bill_gates", connection);
        await using var reader = await command.ExecuteReaderAsync();
        Assert.True(await reader.ReadAsync());
        return (reader.GetString(0), reader.IsDBNull(1) ? null : reader.GetString(1));
    }

    private async Task<object?> ScalarAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }

    private async Task<DateOnly?> ReadBillCursorAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT last_processed_day FROM bill_stream_cursors", connection);
        return await command.ExecuteScalarAsync() is DateOnly day ? day : null;
    }

    private sealed class ApiFixtureCollector(string body = "{\"member\":{\"bioguideId\":\"W000817\"}}") : ICollectorProcess
    {
        public async Task<CollectionResult> RunAsync(CollectionRequest request, CancellationToken cancellationToken)
        {
            var json = Encoding.UTF8.GetBytes(body);
            var hash = Convert.ToHexStringLower(SHA256.HashData(json));
            await using (var file = File.Create(Path.Combine(request.ArtifactDirectory, hash + ".gz")))
            await using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
                await gzip.WriteAsync(json, cancellationToken);
            return Receipt(request) with
            {
                RobotsRequestCount = 0,
                RequestCount = 1,
                Response = new HttpResponseMetadata { StatusCode = 200, ContentType = "application/json", ContentEncodings = [] },
                Capture = new CaptureArtifact { Sha256 = hash, RelativePath = hash + ".gz", ByteLength = json.Length },
                BytesReceived = json.Length
            };
        }
    }

    private sealed class FixtureCollectorWriter
    {
        public async Task<CollectionResult> WriteAsync(CollectionRequest request, string body, DateTimeOffset observedAt,
            CancellationToken cancellationToken)
        {
            var html = Encoding.UTF8.GetBytes($"<html><body><main>{body}</main></body></html>");
            var hash = Convert.ToHexStringLower(SHA256.HashData(html));
            await using (var file = File.Create(Path.Combine(request.ArtifactDirectory, hash + ".gz")))
            await using (var gzip = new GZipStream(file, CompressionLevel.Fastest))
                await gzip.WriteAsync(html, cancellationToken);
            return Receipt(request) with
            {
                ObservedAt = observedAt,
                SentValidators = request.ETag is null && request.LastModified is null ? null :
                    new HttpRequestValidators { ETag = request.ETag, LastModified = request.LastModified },
                Response = new HttpResponseMetadata { StatusCode = 200, ETag = "\"v1\"", ContentType = "text/html; charset=utf-8", ContentEncodings = [] },
                Capture = new CaptureArtifact { Sha256 = hash, RelativePath = hash + ".gz", ByteLength = html.Length },
                BytesReceived = html.Length
            };
        }
    }

    private sealed class StalePredecessorHistoryStore(IDocumentHistoryStore inner, string predecessorAttemptId)
        : IDocumentHistoryStore
    {
        public int ReadCount { get; private set; }

        public async Task<IReadOnlyList<DocumentHistoryObservation>> GetAsync(string sourceId, string requestedUrl,
            int maximumObservations, CancellationToken cancellationToken)
        {
            var observations = await inner.GetAsync(sourceId, requestedUrl, maximumObservations, cancellationToken);
            if (ReadCount++ != 0) return observations;
            return observations.Select(observation => observation.Attempt.AttemptResult.AttemptId == predecessorAttemptId
                ? observation with { Extractions = [] }
                : observation).ToArray();
        }
    }

    private sealed class GatedDocumentHistoryStore(IDocumentHistoryStore inner) : IDocumentHistoryStore
    {
        private int calls;
        public TaskCompletionSource Entered { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        public TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<IReadOnlyList<DocumentHistoryObservation>> GetAsync(string sourceId, string requestedUrl,
            int maximumObservations, CancellationToken cancellationToken)
        {
            if (Interlocked.Increment(ref calls) == 1)
            {
                Entered.TrySetResult();
                await Release.Task.WaitAsync(cancellationToken);
            }
            return await inner.GetAsync(sourceId, requestedUrl, maximumObservations, cancellationToken);
        }
    }

    private sealed class ObservedEvidenceProcessingStore(IEvidenceProcessingStore inner) : IEvidenceProcessingStore
    {
        private int eligibleReads;
        public int EligibleReads => Volatile.Read(ref eligibleReads);
        public TaskCompletionSource<bool> Renewed { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public Task<IReadOnlyList<CollectionChangeEvent>> ListRecentChangesAsync(int limit, CancellationToken cancellationToken) =>
            inner.ListRecentChangesAsync(limit, cancellationToken);

        public Task<IReadOnlyList<EvidenceProcessingRecord>> GetEligibleAsync(int limit, CancellationToken cancellationToken)
        {
            Interlocked.Increment(ref eligibleReads);
            return inner.GetEligibleAsync(limit, cancellationToken);
        }

        public Task<bool> IsPredecessorSettledAsync(string attemptId, CancellationToken cancellationToken) =>
            inner.IsPredecessorSettledAsync(attemptId, cancellationToken);

        public Task<IReadOnlyList<EvidenceProcessingRecord>> GetByJobIdsAsync(IReadOnlyList<string> jobIds,
            CancellationToken cancellationToken) => inner.GetByJobIdsAsync(jobIds, cancellationToken);

        public Task<EvidenceProcessingRecord> EnsureAsync(string jobId, string attemptId, string sourceId,
            string requestedUrl, CancellationToken cancellationToken) =>
            inner.EnsureAsync(jobId, attemptId, sourceId, requestedUrl, cancellationToken);

        public Task<EvidenceProcessingClaim?> TryClaimAsync(string jobId, string attemptId, TimeSpan leaseDuration,
            CancellationToken cancellationToken) => inner.TryClaimAsync(jobId, attemptId, leaseDuration, cancellationToken);

        public async Task<bool> RenewAsync(EvidenceProcessingClaim claim, TimeSpan leaseDuration,
            CancellationToken cancellationToken)
        {
            var renewed = await inner.RenewAsync(claim, leaseDuration, cancellationToken);
            Renewed.TrySetResult(renewed);
            return renewed;
        }

        public Task<bool> WaitForPredecessorAsync(EvidenceProcessingClaim claim, string predecessorAttemptId,
            CancellationToken cancellationToken) =>
            inner.WaitForPredecessorAsync(claim, predecessorAttemptId, cancellationToken);

        public Task<bool> CheckpointAsync(EvidenceProcessingCheckpoint checkpoint, CancellationToken cancellationToken) =>
            inner.CheckpointAsync(checkpoint, cancellationToken);

        public Task<bool> ReleaseAsync(EvidenceProcessingClaim claim, CancellationToken cancellationToken) =>
            inner.ReleaseAsync(claim, cancellationToken);
    }
}
