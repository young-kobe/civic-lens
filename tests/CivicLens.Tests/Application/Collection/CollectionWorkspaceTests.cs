using CivicLens.Application.Paging;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Documents;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using CivicLens.Core.Review;

namespace CivicLens.Tests.Application.Collection;

public sealed class CollectionWorkspaceTests
{
    private static readonly ReviewActor Owner = new("auth0|owner", ReviewRole.Owner);

    [Theory]
    [InlineData(ReviewRole.Reviewer, "auth0|friend")]
    [InlineData(ReviewRole.Owner, "")]
    [InlineData((ReviewRole)99, "auth0|owner")]
    public async Task EveryCollectionAndPreparationEntryPointRejectsUnauthorizedActorsBeforeStorage(ReviewRole role, string subject)
    {
        var configuration = new CollectionConfiguration { Version = 2, People = [], Sources = [] };
        var workspace = new CollectionWorkspace(configuration, Path.GetTempPath(), null!, null!, null!, null!, null!, null!, null!);
        var actor = new ReviewActor(subject, role);
        Assert.Throws<UnauthorizedAccessException>(() => workspace.GetConfiguration(actor));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => workspace.GetSourcesAsync(actor, null, null));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => workspace.GetSourceHealthAsync(actor, default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => workspace.EnqueueAsync(actor, "source", "bad", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => workspace.CancelAsync(actor, "bad", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => workspace.RecollectAsync(actor, "bad", "bad", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => workspace.AdmitArticlesAsync(actor, "bad", "bad", "bad", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => workspace.ExtractAsync(actor, "bad", "bad", default));
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => workspace.CompareAsync(actor, "bad", "bad", "bad", default));
    }

    [Fact]
    public async Task RecollectUsesRetainedArticleUrlWithCurrentFeedSourceSettingsAndStableIdempotencyKey()
    {
        var retainedUrl = "https://example.test/news/article";
        var oldConfiguration = Configuration(Source(CollectionMode.Feed, "https://example.test/news/feed", maxRequests: 2));
        var oldDefinition = CollectionJobDefinition.FromConfiguration(oldConfiguration, "source") with
        {
            Mode = CollectionMode.Page,
            Url = retainedUrl,
            ETag = null,
            LastModified = null
        };
        var oldJob = Job(oldDefinition);
        var jobs = new RecordingJobStore(oldJob);
        var currentSource = Source(CollectionMode.Feed, "https://example.test/news/feed", maxRequests: 7) with
        {
            MinDelayMilliseconds = 2200,
            AllowedPathPrefix = "/news"
        };
        var workspace = new CollectionWorkspace(Configuration(currentSource), Path.GetTempPath(), jobs, null!, null!, null!, null!, null!, null!);

        await workspace.RecollectAsync(Owner, oldJob.JobId, "0123456789abcdef0123456789abcdef", default);
        await workspace.RecollectAsync(Owner, oldJob.JobId, "0123456789abcdef0123456789abcdef", default);

        Assert.Equal(2, jobs.ConfiguredSources.Count);
        var definition = jobs.Definitions[0];
        Assert.Equal(CollectionMode.Page, definition.Mode);
        Assert.Equal(retainedUrl, definition.Url);
        Assert.Equal(7, definition.MaxRequests);
        Assert.Equal(2200, definition.MinDelayMilliseconds);
        Assert.Single(jobs.IdempotencyKeys.Distinct());
        Assert.Equal(jobs.IdempotencyKeys[0], jobs.IdempotencyKeys[1]);
    }

    [Theory]
    [InlineData(false, "https://example.test/outside/article")]
    [InlineData(true, "https://example.test/news/article")]
    public async Task RecollectRejectsRetainedUrlOutsideCurrentScopeOrDisabledSource(bool disabled, string retainedUrl)
    {
        var oldSource = Source(CollectionMode.Feed, "https://example.test/news/feed", 2) with { AllowedPathPrefix = "/" };
        var prior = Job(CollectionJobDefinition.FromConfiguration(Configuration(oldSource), "source") with
        {
            Mode = CollectionMode.Page,
            Url = retainedUrl,
            ETag = null,
            LastModified = null
        });
        var currentSource = Source(CollectionMode.Feed, "https://example.test/news/feed", 3) with
        {
            Enabled = !disabled,
            AllowedPathPrefix = "/news"
        };
        var jobs = new RecordingJobStore(prior);
        var workspace = new CollectionWorkspace(Configuration(currentSource), Path.GetTempPath(), jobs, null!, null!, null!, null!, null!, null!);

        await Assert.ThrowsAsync<ArgumentException>(() => workspace.RecollectAsync(Owner, prior.JobId,
            "0123456789abcdef0123456789abcdef", default));
        Assert.Empty(jobs.ConfiguredSources);
    }

    [Fact]
    public async Task SourcesLoadsSelectedRecentJobAndEachDistinctReceiptlessAttemptOnce()
    {
        var attemptId = "attempt-one";
        var request = Request(attemptId);
        var definition = CollectionJobDefinition.FromConfiguration(Configuration(Source(CollectionMode.Page,
            "https://example.test/news", 2)), "source");
        var selected = Job(definition) with
        {
            Attempts = [JobAttempt(attemptId, request), JobAttempt(attemptId, request)]
        };
        var jobs = new CountingJobStore([selected]);
        var retained = new AttemptsStore(Captured(attemptId, "https://example.test/news"));
        var workspace = Workspace(jobs, retained, new HistoryStore([]));

        var result = await workspace.GetSourcesAsync(Owner, selected.JobId, null);

        Assert.Same(selected, result.SelectedJob);
        Assert.Equal(0, jobs.GetCalls);
        Assert.Equal(1, jobs.ListCalls);
        Assert.Equal(0, jobs.LatestCalls);
        Assert.Equal(new[] { attemptId }, retained.ReadIds);
        Assert.Contains(attemptId, result.RetainedAttempts.Keys);
    }

    [Fact]
    public async Task SourcesReusesPageAttemptAlreadyLoadedForHistory()
    {
        var attemptId = "attempt-one";
        var request = Request(attemptId);
        var definition = CollectionJobDefinition.FromConfiguration(Configuration(Source(CollectionMode.Page,
            "https://example.test/news", 2)), "source");
        var selected = Job(definition) with { Attempts = [JobAttempt(attemptId, request)] };
        var jobs = new CountingJobStore([selected]);
        var stored = Captured(attemptId, "https://example.test/news");
        var history = new HistoryStore([new DocumentHistoryObservation(stored, [])]);
        var attempts = new AttemptsStore(stored);

        var result = await Workspace(jobs, attempts, history).GetSourcesAsync(Owner, selected.JobId, null);

        Assert.Empty(attempts.ReadIds);
        Assert.Same(stored, result.RetainedAttempts[attemptId]);
    }

    [Fact]
    public async Task SourcesFetchesAnOlderJobOnceAndRejectsMismatchedRetainedAttempt()
    {
        var definition = CollectionJobDefinition.FromConfiguration(Configuration(Source(CollectionMode.Page,
            "https://example.test/news", 2)), "source");
        var selected = Job(definition) with
        {
            JobId = "1123456789abcdef0123456789abcdef",
            Attempts = [JobAttempt("attempt-one", Request("attempt-one"))]
        };
        var jobs = new CountingJobStore([], selected);
        var retained = new AttemptsStore(Captured("attempt-one", "https://example.test/news/other"));
        var workspace = Workspace(jobs, retained, new HistoryStore([]));

        await Assert.ThrowsAsync<InvalidDataException>(() => workspace.GetSourcesAsync(Owner, selected.JobId, null));
        Assert.Equal(1, jobs.GetCalls);
        Assert.Equal(1, jobs.ListCalls);
        Assert.Equal(new[] { "attempt-one" }, retained.ReadIds);
    }

    [Fact]
    public async Task SourcesPreservesHistoryUnavailableStateAndOwnerIsCheckedBeforeReads()
    {
        var definition = CollectionJobDefinition.FromConfiguration(Configuration(Source(CollectionMode.Page,
            "https://example.test/news", 2)), "source");
        var selected = Job(definition) with { Attempts = [JobAttempt("attempt-one", Request("attempt-one"))] };
        var jobs = new CountingJobStore([selected]);
        var retained = new AttemptsStore(Captured("attempt-one", "https://example.test/news"));
        var history = new HistoryStore([], new IOException("storage unavailable"));
        var workspace = Workspace(jobs, retained, history);

        var denied = new ReviewActor("auth0|reader", ReviewRole.Reviewer);
        await Assert.ThrowsAsync<UnauthorizedAccessException>(() => workspace.GetSourcesAsync(denied, selected.JobId, default));
        Assert.Equal(0, jobs.ListCalls);
        var result = await workspace.GetSourcesAsync(Owner, selected.JobId, null);

        Assert.True(result.HistoryUnavailable);
        Assert.Null(result.History);
        Assert.Equal(new[] { "attempt-one" }, retained.ReadIds);
    }

    [Fact]
    public async Task CompareLoadsEachExtractionOnceBeforeSavingComparison()
    {
        var definition = CollectionJobDefinition.FromConfiguration(Configuration(Source(CollectionMode.Page,
            "https://example.test/news", 2)), "source");
        var job = Job(definition);
        var before = Extraction("before", DateTimeOffset.UnixEpoch);
        var after = Extraction("after", DateTimeOffset.UnixEpoch.AddDays(1));
        var extractions = new CountingExtractionStore(before, after);
        var comparisons = new ComparisonStore();
        var jobs = new CountingJobStore([], job);
        var workspace = new CollectionWorkspace(Configuration(Source(CollectionMode.Page,
            "https://example.test/news", 2)), Path.GetTempPath(), jobs, null!, null!, extractions,
            new CompareDocuments(extractions, comparisons), new GetDocumentHistory(new HistoryStore([])),
            new AttemptsStore(null));

        var comparison = await workspace.CompareAsync(Owner, job.JobId, before.ExtractionId, after.ExtractionId, default);

        Assert.Same(comparison, comparisons.Saved);
        Assert.Equal(new[] { before.ExtractionId, after.ExtractionId }, extractions.ReadIds);
    }

    private static CollectionConfiguration Configuration(WatchedSourceConfiguration source) => new()
    {
        Version = 1,
        People = [new PersonConfiguration { Id = "person", Name = "Official" }],
        Sources = [source with { PersonIds = ["person"] }]
    };

    private static WatchedSourceConfiguration Source(CollectionMode mode, string url, int maxRequests) => new()
    {
        Id = "source",
        Url = url,
        AllowedOrigin = "https://example.test",
        AllowedPathPrefix = "/news",
        Mode = mode,
        MaxRequests = maxRequests
    };

    private static CollectionJobRecord Job(CollectionJobDefinition definition) => new(
        "0123456789abcdef0123456789abcdef", definition, "old-key", CollectionJobState.Succeeded,
        DateTimeOffset.UtcNow, null, false, 0, 0, 0, []);

    private static CollectionWorkspace Workspace(CountingJobStore jobs, AttemptsStore attempts, HistoryStore history) =>
        new(Configuration(Source(CollectionMode.Page, "https://example.test/news", 2)), Path.GetTempPath(),
            jobs, null!, null!, null!, null!, new GetDocumentHistory(history), attempts);

    private static CollectionJobAttempt JobAttempt(string id, CollectionRequest request) =>
        new(id, request, null, DateTimeOffset.UtcNow, null);

    private static CollectionRequest Request(string id) => new()
    {
        JobId = "0123456789abcdef0123456789abcdef",
        SourceId = "source",
        Url = "https://example.test/news",
        AllowedOrigin = "https://example.test",
        AllowedPathPrefix = "/news",
        ArtifactDirectory = Path.GetTempPath()
    };

    private static StoredCollectionAttempt Captured(string id, string url) => new(
        new CapturedAttemptResult(id, "source", url, url, DateTimeOffset.UtcNow,
            new CollectionResponse(200, null, null, "text/plain", []), new CaptureIdentity(new string('a', 64), 1)), null, null);

    private static DocumentExtraction Extraction(string text, DateTimeOffset observedAt)
    {
        var attempt = new CapturedAttemptResult(text, "source", "https://example.test/news", "https://example.test/news",
            observedAt, new CollectionResponse(200, null, null, "text/plain", []),
            new CaptureIdentity(new string('b', 64), 1));
        return new DocumentExtraction(attempt, "parser-v1", "normalization-v1", text);
    }

    private sealed class RecordingJobStore(CollectionJobRecord job) : ICollectionJobStore
    {
        public List<(ConfiguredCollectionSource Source, string Key)> ConfiguredSources { get; } = [];
        public List<CollectionJobDefinition> Definitions { get; } = [];
        public List<string> IdempotencyKeys => ConfiguredSources.Select(item => item.Key).ToList();
        public Task<CollectionJobRecord> EnqueueAsync(ConfiguredCollectionSource source, string idempotencyKey, CancellationToken cancellationToken)
        {
            var definition = source.CreateJobDefinition(DateOnly.FromDateTime(DateTime.UtcNow));
            ConfiguredSources.Add((source, idempotencyKey));
            Definitions.Add(definition);
            return Task.FromResult(job);
        }
        public Task<CollectionJobRecord> EnqueueAsync(CollectionJobDefinition definition, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobRecord?> GetAsync(string jobId, CancellationToken cancellationToken) => Task.FromResult<CollectionJobRecord?>(jobId == job.JobId ? job : null);
        public Task<IReadOnlyList<CollectionJobRecord>> ListAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobPage> ListPageAsync(PageCursor? cursor, int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<IReadOnlyList<CollectionJobRecord>> ListLatestBySourceAsync(IReadOnlyCollection<SourceCheckTarget> targets, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobActivity> ListActivityAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobRecord?> CancelAsync(string jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobClaim?> TryClaimAsync(string jobId, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobRenewal> RenewAsync(CollectionJobLease jobLease, CollectionCollectorLease? collectorLease, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobStartResult> TryStartAttemptAsync(CollectionJobLease jobLease, string artifactDirectory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ReleaseCollectorAsync(CollectionJobLease jobLease, CollectionCollectorLease collectorLease, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SettleAttemptAsync(CollectionJobLease jobLease, string attemptId, CollectionAttemptResolution resolution, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ReleaseClaimAsync(CollectionJobLease jobLease, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class CountingJobStore(IReadOnlyList<CollectionJobRecord> recent, CollectionJobRecord? older = null) : ICollectionJobStore
    {
        public int ListCalls { get; private set; }
        public int LatestCalls { get; private set; }
        public int GetCalls { get; private set; }
        public Task<IReadOnlyList<CollectionJobRecord>> ListAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobPage> ListPageAsync(PageCursor? cursor, int limit, CancellationToken cancellationToken)
        {
            ListCalls++;
            Assert.Equal(CollectionWorkspace.RecentJobLimit, limit);
            return Task.FromResult(new CollectionJobPage(recent, null, null));
        }
        public Task<IReadOnlyList<CollectionJobRecord>> ListLatestBySourceAsync(IReadOnlyCollection<SourceCheckTarget> targets, CancellationToken cancellationToken)
        {
            LatestCalls++;
            return Task.FromResult<IReadOnlyList<CollectionJobRecord>>([]);
        }
        public Task<CollectionJobActivity> ListActivityAsync(int limit, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobRecord?> GetAsync(string jobId, CancellationToken cancellationToken)
        {
            GetCalls++;
            return Task.FromResult(older?.JobId == jobId ? older : null);
        }
        public Task<CollectionJobRecord> EnqueueAsync(ConfiguredCollectionSource source, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobRecord> EnqueueAsync(CollectionJobDefinition definition, string idempotencyKey, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobRecord?> CancelAsync(string jobId, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobClaim?> TryClaimAsync(string jobId, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobRenewal> RenewAsync(CollectionJobLease jobLease, CollectionCollectorLease? collectorLease, TimeSpan leaseDuration, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<CollectionJobStartResult> TryStartAttemptAsync(CollectionJobLease jobLease, string artifactDirectory, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ReleaseCollectorAsync(CollectionJobLease jobLease, CollectionCollectorLease collectorLease, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> SettleAttemptAsync(CollectionJobLease jobLease, string attemptId, CollectionAttemptResolution resolution, CancellationToken cancellationToken) => throw new NotSupportedException();
        public Task<bool> ReleaseClaimAsync(CollectionJobLease jobLease, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class AttemptsStore(StoredCollectionAttempt? result) : ICollectionAttemptStore
    {
        public List<string> ReadIds { get; } = [];
        public Task<StoredCollectionAttempt?> GetAsync(string attemptId, CancellationToken cancellationToken)
        {
            ReadIds.Add(attemptId);
            return Task.FromResult(result?.AttemptResult.AttemptId == attemptId ? result : null);
        }
        public Task<CollectionImportDecision> ImportAtomicallyAsync(CollectionAttemptImport attempt, CancellationToken cancellationToken) => throw new NotSupportedException();
    }

    private sealed class HistoryStore(IReadOnlyList<DocumentHistoryObservation> observations, Exception? failure = null) : IDocumentHistoryStore
    {
        public Task<IReadOnlyList<DocumentHistoryObservation>> GetAsync(string sourceId, string requestedUrl,
            int maximumObservations, CancellationToken cancellationToken) => failure is null
                ? Task.FromResult(observations)
                : Task.FromException<IReadOnlyList<DocumentHistoryObservation>>(failure);
    }

    private sealed class CountingExtractionStore(params DocumentExtraction[] values) : IDocumentExtractionStore
    {
        public List<string> ReadIds { get; } = [];
        public Task<DocumentExtraction?> GetAsync(string extractionId, CancellationToken cancellationToken)
        {
            ReadIds.Add(extractionId);
            return Task.FromResult(values.SingleOrDefault(value => value.ExtractionId == extractionId));
        }
        public Task<DocumentExtraction> SaveAsync(DocumentExtraction extraction, CancellationToken cancellationToken) => Task.FromResult(extraction);
    }

    private sealed class ComparisonStore : IDocumentComparisonStore
    {
        public DocumentComparison? Saved { get; private set; }
        public Task<DocumentComparison?> GetAsync(string comparisonId, CancellationToken cancellationToken) => Task.FromResult(Saved);
        public Task<DocumentComparison> SaveAsync(DocumentComparison comparison, CancellationToken cancellationToken)
        {
            Saved = comparison;
            return Task.FromResult(comparison);
        }
    }
}
