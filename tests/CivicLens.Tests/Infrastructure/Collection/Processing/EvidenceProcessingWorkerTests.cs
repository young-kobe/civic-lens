using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Documents;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Core.Registry;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using CivicLens.Infrastructure.Collection.Processing;
using CivicLens.Infrastructure.Documents;
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
    private readonly string schema = "processing_worker_" + Guid.NewGuid().ToString("N");
    private readonly string artifactRoot = Path.Combine(Path.GetTempPath(), "civic-processing-" + Guid.NewGuid().ToString("N"));
    private string connectionString = null!;
    private PostgresCollectionJobStore jobs = null!;
    private PostgresCollectionAttemptStore attempts = null!;
    private PostgresEvidenceProcessingStore processing = null!;
    private PostgresDocumentExtractionStore extractions = null!;
    private PostgresDocumentComparisonStore comparisons = null!;
    private ExtractDocument extract = null!;

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE SCHEMA {schema}", connection);
        await command.ExecuteNonQueryAsync();
        connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { SearchPath = schema }.ConnectionString;
        jobs = PostgresCollectionJobStore.FromConnectionString(connectionString);
        attempts = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        processing = PostgresEvidenceProcessingStore.FromConnectionString(connectionString);
        extractions = PostgresDocumentExtractionStore.FromConnectionString(connectionString);
        comparisons = PostgresDocumentComparisonStore.FromConnectionString(connectionString);
        extract = new ExtractDocument(attempts, new CaptureDocumentTextExtractor(), extractions);
        Directory.CreateDirectory(artifactRoot);
        await attempts.MigrateAsync();
    }

    public async Task DisposeAsync()
    {
        if (Directory.Exists(artifactRoot)) Directory.Delete(artifactRoot, recursive: true);
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP SCHEMA IF EXISTS {schema} CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task UpgradeRegistersLegacySuccessfulJobsAndResumesTheirPipeline()
    {
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new CollectionAttemptDbContext(options);
        await db.GetService<IMigrator>().MigrateAsync("20261008190000_DurableEvidenceProcessing");
        var beforeJob = await CollectAsync("earlier", "Earlier wording.");
        var afterJob = await CollectAsync("later", "Later wording.");
        var beforeAttempt = Assert.Single(beforeJob.Attempts).AttemptId;
        var afterAttempt = Assert.Single(afterJob.Attempts).AttemptId;

        await attempts.MigrateAsync();
        var recovered = await processing.GetByJobIdsAsync([beforeJob.JobId, afterJob.JobId], default);
        Assert.Equal(2, recovered.Count);
        Assert.All(recovered, record => Assert.Equal("prepare", record.AttemptId));
        var completed = await ProcessUntilTerminalAsync(Worker(processing), afterJob.JobId, afterAttempt);
        Assert.Equal(EvidenceProcessingOutcome.Changed, completed.Outcome);
        var beforeRecord = await processing.GetByAttemptIdAsync(beforeAttempt, default);
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
        var manualReceipt = await CapturedReceiptAsync(manualRequest, "Earlier manual wording.", DateTimeOffset.UtcNow.AddMinutes(-2));
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
        var record = await processing.GetByAttemptIdAsync(attemptId, default);
        Assert.Equal(EvidenceProcessingStatus.Succeeded, record!.Status);
        Assert.Equal(EvidenceProcessingOutcome.Baseline, record.Outcome);
        Assert.NotNull(await extractions.GetAsync(record.ExtractionId!, default));
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
    public async Task ResolvedNotModifiedObservationUsesItsLinkedCaptureAndFailedObservationStartsHistoryGap()
    {
        var captured = await CollectAsync("before-304", "Before conditional check.");
        var priorAttempt = Assert.Single(captured.Attempts).AttemptId;
        var conditionalJob = await CollectAsync("not-modified", body: null, etag: "\"v1\"");
        var notModified = await attempts.GetAsync(Assert.Single(conditionalJob.Attempts).AttemptId, default);
        Assert.Equal(priorAttempt, notModified!.PriorCapturedAttempt!.AttemptId);
        var after304Job = await CollectAsync("after-304", "After conditional check.");
        var after304Attempt = Assert.Single(after304Job.Attempts).AttemptId;
        var after304 = await ProcessUntilTerminalAsync(Worker(processing), after304Job.JobId, after304Attempt);
        Assert.Equal(EvidenceProcessingOutcome.Changed, after304.Outcome);
        var originalRecord = await processing.GetByAttemptIdAsync(priorAttempt, default);
        var originalExtraction = await extractions.GetAsync(originalRecord!.ExtractionId!, default);
        var after304Comparison = await comparisons.GetAsync(after304.ComparisonId!, default);
        Assert.Equal(originalExtraction!.ExtractionId, after304Comparison!.BeforeExtractionId);
        Assert.NotEmpty(after304Comparison.Hunks);

        var earlierCapture = await CollectAsync("before-failure", "Text before failed observation.");
        var failedJob = await CollectAsync("failed-observation", body: null, fail: true);
        Assert.NotEqual(CollectionJobState.Succeeded, failedJob.State);
        var resumedJob = await CollectAsync("after-failure", "Text after failed observation.");
        var resumedAttempt = Assert.Single(resumedJob.Attempts).AttemptId;
        var resumed = await ProcessUntilTerminalAsync(Worker(processing), resumedJob.JobId, resumedAttempt);
        Assert.Equal(EvidenceProcessingOutcome.Baseline, resumed.Outcome);
        Assert.Equal("historyGap", resumed.ErrorCode);
        Assert.NotEmpty(earlierCapture.Attempts);
    }

    private async Task<CollectionJobRecord> CollectAsync(string key, string? body, string? etag = null, bool fail = false)
    {
        var definition = ConfiguredDefinition(etag);
        var job = await jobs.EnqueueAsync(definition, key, default);
        var collector = new FixtureCollector(body, fail, Interlocked.Increment(ref observationTick));
        var runner = new RunCollectionJob(jobs, attempts, new FileCollectionReceiptHandoffStore(artifactRoot),
            new CaptureArtifactVerifier(), collector);
        var result = await runner.ExecuteAsync(job.JobId, artifactRoot, TimeSpan.FromSeconds(60), default);
        Assert.True(result.Status is CollectionJobRunStatus.Completed or CollectionJobRunStatus.AttemptFailed,
            $"Collection returned {result.Status}: {result.BlockReason}, retry {result.RetryAt:O}.");
        return (await jobs.GetAsync(job.JobId, default))!;
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
            await Task.Delay(100);
        }
        throw new InvalidOperationException($"Processing attempt '{attemptId}' did not reach a terminal state.");
    }

    private EvidenceProcessingWorker Worker(IEvidenceProcessingStore processingStore, IDocumentHistoryStore? historyStore = null) => new(jobs, attempts, jobs,
        processingStore, extract, new GetDocumentHistory(historyStore ?? PostgresDocumentHistoryStore.FromConnectionString(connectionString)),
        new CompareDocuments(extractions, comparisons));

    private CollectionJobDefinition ConfiguredDefinition(string? etag = null)
    {
        var source = new ConfiguredCollectionSource(Configuration(etag), "source");
        return source.CreateJobDefinition(DateOnly.FromDateTime(DateTime.UtcNow));
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
    private static long observationTick;

    private sealed class FixtureCollector(string? body, bool fail, long sequence) : ICollectorProcess
    {
        public async Task<CollectionResult> RunAsync(CollectionRequest request, CancellationToken cancellationToken)
        {
            var observedAt = DateTimeOffset.UtcNow.AddTicks(sequence);
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
}
