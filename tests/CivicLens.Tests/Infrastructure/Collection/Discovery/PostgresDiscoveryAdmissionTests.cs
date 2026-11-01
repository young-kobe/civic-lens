using CivicLens.Tests.Fixtures;
using System.Data.Common;
using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.Discovery;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresDiscoveryAdmissionTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly string artifacts = Path.Combine(Path.GetTempPath(), "civic-admission-" + Guid.NewGuid().ToString("N"));
    private PostgresCollectionJobStore jobs = null!;
    private PostgresCollectionAttemptStore attempts = null!;
    private int feedSequence;
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        jobs = PostgresCollectionJobStore.FromConnectionString(connectionString);
        attempts = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        Directory.CreateDirectory(artifacts);
    }

    public async Task DisposeAsync()
    {
        Directory.Delete(artifacts, true);
        await postgres.DropDatabaseAsync(connectionString);
    }

    [Theory]
    [InlineData(8, 100, 100)]
    [InlineData(100, 40, 100)]
    [InlineData(100, 100, 16)]
    public async Task AdmissionReplaysDeduplicatesAndReservesFullRetryBudgets(int requests, long bytes, int seconds)
    {
        var urls = new[] { "https://example.test/articles/one", "https://example.test/articles/two", "https://example.test/articles/three" };
        var attemptId = await SaveFeedAsync(urls);
        var policy = new DiscoveryAdmissionPolicy
        {
            MaxJobs = 10,
            MaxTotalRequests = requests,
            MaxTotalBytes = bytes,
            MaxTotalTimeoutSeconds = seconds
        };
        var source = Source(policy: policy);

        var first = await jobs.AdmitAsync(source, attemptId, "batch-one", default);
        var replay = await jobs.AdmitAsync(source, attemptId, "batch-one", default);

        Assert.Equal(2, first.AdmittedCount);
        Assert.Equal(1, first.DeferredCount);
        Assert.Equal(0, first.DuplicateCount);
        Assert.Equal(first.Jobs, replay.Jobs);
        Assert.Equal(first.DeferredCount, replay.DeferredCount);
        Assert.Equal(first.DuplicateCount, replay.DuplicateCount);
        Assert.Equal(urls.Take(2), first.Jobs.Select(job => job.Url));
        var storedJobs = await Task.WhenAll(first.Jobs.Select(job => jobs.GetAsync(job.JobId, default)));
        Assert.All(storedJobs, job => Assert.Equal(CollectionJobState.Pending, job!.State));

        var sameCandidate = await SaveFeedAsync([urls[0]]);
        var duplicate = await jobs.AdmitAsync(Source(), sameCandidate, "batch-two", default);
        Assert.Equal(0, duplicate.AdmittedCount);
        Assert.Equal(1, duplicate.DuplicateCount);

        await Assert.ThrowsAsync<ArgumentException>(() => jobs.AdmitAsync(Source(maxBytes: 11, policy: policy),
            attemptId, "batch-one", default));
    }

    [Fact]
    public async Task ConflictingReuseOfAnAdmissionKeyFails()
    {
        var url = "https://example.test/articles/one";
        var first = await SaveFeedAsync([url]);
        var other = await SaveFeedAsync([url]);
        await jobs.AdmitAsync(Source(), first, "reused-key", default);

        await Assert.ThrowsAsync<ArgumentException>(() => jobs.AdmitAsync(Source(), other, "reused-key", default));
    }

    [Fact]
    public async Task AdmissionChecksExistingCandidatesWithOneQueryAndReplaysWithoutRechecking()
    {
        var urls = Enumerable.Range(0, 40)
            .Select(index => $"https://example.test/articles/{index:D2}")
            .ToArray();
        var firstAttempt = await SaveFeedAsync(urls.Take(10).ToArray());
        var firstBatch = await jobs.AdmitAsync(Source(), firstAttempt, "candidate-seed", default);
        Assert.Equal(10, firstBatch.AdmittedCount);

        var attempt = await SaveFeedAsync(urls);
        var counter = new CandidateSelectCounter();
        var countedFactory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString)
                .AddInterceptors(counter).Options);
        var countedJobs = new PostgresCollectionJobStore(countedFactory);
        var source = Source(policy: new DiscoveryAdmissionPolicy { MaxJobs = 40 });

        var admitted = await countedJobs.AdmitAsync(source, attempt, "candidate-mixed", default);
        Assert.Equal(10, admitted.DuplicateCount);
        Assert.Equal(30, admitted.AdmittedCount);
        Assert.Equal(urls.Skip(10), admitted.Jobs.Select(job => job.Url));
        Assert.Equal(1, counter.CandidateSelectCount);

        var replay = await countedJobs.AdmitAsync(source, attempt, "candidate-mixed", default);
        Assert.Equal(admitted.Jobs, replay.Jobs);
        Assert.Equal(admitted.DeferredCount, replay.DeferredCount);
        Assert.Equal(admitted.DuplicateCount, replay.DuplicateCount);
        Assert.Equal(1, counter.CandidateSelectCount);
    }

    [Fact]
    public async Task AutomaticSourceCheckRequeuesTerminalKnownUrlOnceWithinItsBatchBudget()
    {
        var url = "https://example.test/articles/known";
        var firstAttempt = await SaveFeedAsync([url]);
        var first = await jobs.AdmitAsync(Source(), firstAttempt, "manual-seed", default);
        var initialJob = Assert.Single(first.Jobs);
        Assert.NotNull(await jobs.CancelAsync(initialJob.JobId, default));

        var template = Template();
        var configuration = new CollectionConfiguration
        {
            People = [new PersonConfiguration { Id = "person", Name = "Official" }],
            Sources = [new WatchedSourceConfiguration
            {
                Id = "source", Coverage = [new SourceCoverageConfiguration { PersonId = "person" }], Url = template.Url,
                AllowedOrigin = template.AllowedOrigin, AllowedPathPrefix = template.AllowedPathPrefix,
                Mode = CollectionMode.Feed, MaxRequests = template.MaxRequests, MaxBytes = template.MaxBytes,
                TimeoutSeconds = template.TimeoutSeconds, MinDelayMilliseconds = template.MinDelayMilliseconds,
                JobPolicy = template.Policy,
                AdmissionPolicy = new DiscoveryAdmissionPolicy { MaxJobs = 1 }
            }]
        };
        var configured = new ConfiguredCollectionSource(configuration, "source", new DateOnly(2026, 10, 8));
        var secondAttempt = await SaveFeedAsync([url]);
        var next = await jobs.AdmitAutomaticCheckAsync(configured, secondAttempt, "automatic-check-two", default);
        var replay = await jobs.AdmitAutomaticCheckAsync(configured, secondAttempt, "automatic-check-two", default);
        Assert.Single(next.Jobs);
        Assert.Equal(1, next.AdmittedCount);
        Assert.Equal(0, next.DuplicateCount);
        Assert.Equal(next.Jobs, replay.Jobs);

        var nextJob = Assert.Single(next.Jobs);
        var activeAttempt = await SaveFeedAsync([url]);
        var activeDuplicate = await jobs.AdmitAutomaticCheckAsync(configured, activeAttempt, "automatic-check-active", default);
        Assert.Equal(0, activeDuplicate.AdmittedCount);
        Assert.Equal(1, activeDuplicate.DuplicateCount);
        Assert.NotNull(await jobs.CancelAsync(nextJob.JobId, default));
        var thirdAttempt = await SaveFeedAsync([url]);
        var fourthAttempt = await SaveFeedAsync([url]);
        var concurrent = await Task.WhenAll(
            jobs.AdmitAutomaticCheckAsync(configured, thirdAttempt, "automatic-check-three", default),
            jobs.AdmitAutomaticCheckAsync(configured, fourthAttempt, "automatic-check-four", default));
        Assert.Equal(1, concurrent.Sum(result => result.AdmittedCount));
        Assert.Equal(1, concurrent.Sum(result => result.DuplicateCount));
        var newlyAdmitted = Assert.Single(concurrent.SelectMany(result => result.Jobs));
        Assert.NotNull(await jobs.CancelAsync(newlyAdmitted.JobId, default));

        var fifthAttempt = await SaveFeedAsync([url]);
        var capped = await jobs.AdmitAutomaticCheckAsync(configured, fifthAttempt, "automatic-check-five", default);
        Assert.Equal(1, capped.AdmittedCount);
        Assert.Equal(0, capped.DeferredCount);
        Assert.Equal(0, capped.DuplicateCount);
    }

    [Fact]
    public async Task ANewUrlTakesTheSlotBeforeARecheckOfAKnownUrlSoRechecksCannotStarveNewItems()
    {
        var known = "https://example.test/articles/known";
        var fresh = "https://example.test/articles/fresh";
        var seeded = await jobs.AdmitAsync(Source(), await SaveFeedAsync([known]), "manual-seed", default);
        Assert.NotNull(await jobs.CancelAsync(Assert.Single(seeded.Jobs).JobId, default));

        var configured = Source(policy: new DiscoveryAdmissionPolicy { MaxJobs = 1 });
        var result = await jobs.AdmitAutomaticCheckAsync(configured, await SaveFeedAsync([known, fresh]), "automatic-check", default);

        Assert.Equal(fresh, Assert.Single(result.Jobs).Url);
        Assert.Equal(1, result.DeferredCount);
    }

    [Fact]
    public async Task NewUrlsThenRechecksAreAdmittedEachGroupInFeedOrderAndAReplayReturnsTheSameResult()
    {
        var knownOne = "https://example.test/articles/known-one";
        var knownTwo = "https://example.test/articles/known-two";
        var fresh = "https://example.test/articles/fresh";
        var seeded = await jobs.AdmitAsync(Source(), await SaveFeedAsync([knownOne, knownTwo]), "manual-seed", default);
        foreach (var job in seeded.Jobs)
            Assert.NotNull(await jobs.CancelAsync(job.JobId, default));

        var configured = Source(policy: new DiscoveryAdmissionPolicy { MaxJobs = 2 });
        var attempt = await SaveFeedAsync([knownOne, knownTwo, fresh]);
        var result = await jobs.AdmitAutomaticCheckAsync(configured, attempt, "automatic-check", default);
        var replay = await jobs.AdmitAutomaticCheckAsync(configured, attempt, "automatic-check", default);

        Assert.Equal([fresh, knownOne], result.Jobs.Select(job => job.Url));
        Assert.Equal(1, result.DeferredCount);
        Assert.Equal(result.Jobs, replay.Jobs);
        Assert.Equal(result.DeferredCount, replay.DeferredCount);
    }

    private sealed class CandidateSelectCounter : DbCommandInterceptor
    {
        public int CandidateSelectCount { get; private set; }

        public override ValueTask<InterceptionResult<DbDataReader>> ReaderExecutingAsync(DbCommand command,
            CommandEventData eventData, InterceptionResult<DbDataReader> result,
            CancellationToken cancellationToken = default)
        {
            if (command.CommandText.TrimStart().StartsWith("SELECT", StringComparison.OrdinalIgnoreCase) &&
                command.CommandText.Contains("collection_candidate_jobs", StringComparison.Ordinal))
                CandidateSelectCount++;
            return ValueTask.FromResult(result);
        }
    }

    [Fact]
    public async Task ConcurrentFeedAndHtmlAttemptsCreateOnlyOneJobForAnExactSourceUrlPair()
    {
        var url = "https://example.test/articles/shared";
        var firstAttempt = await SaveFeedAsync([url]);
        var secondAttempt = await SaveDiscoveryAsync([url], CollectionMode.Html);

        var results = await Task.WhenAll(
            jobs.AdmitAsync(Source(), firstAttempt, "concurrent-one", default),
            jobs.AdmitAsync(Source(CollectionMode.Html), secondAttempt, "concurrent-two", default));

        Assert.Equal(1, results.Sum(result => result.AdmittedCount));
        Assert.Equal(1, results.Sum(result => result.DuplicateCount));
        Assert.Single(results.SelectMany(result => result.Jobs).Select(job => job.JobId).Distinct());
    }

    [Fact]
    public async Task FailedAdmissionRollsBackJobsBindingsAndBatchAndCanRetry()
    {
        var attemptId = await SaveFeedAsync(["https://example.test/articles/one"]);
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var trigger = new NpgsqlCommand("""
            CREATE FUNCTION fail_admission() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'injected admission failure'; END $$;
            CREATE TRIGGER fail_admission BEFORE INSERT ON collection_admission_batches
            FOR EACH ROW EXECUTE FUNCTION fail_admission();
            """, connection))
            await trigger.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() => jobs.AdmitAsync(Source(), attemptId, "rollback", default));
        foreach (var table in new[] { "collection_jobs", "collection_candidate_jobs", "collection_admission_batches" })
        {
            await using var count = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection);
            Assert.Equal(0L, await count.ExecuteScalarAsync());
        }
        await using (var drop = new NpgsqlCommand("DROP TRIGGER fail_admission ON collection_admission_batches", connection))
            await drop.ExecuteNonQueryAsync();
        Assert.Single((await jobs.AdmitAsync(Source(), attemptId, "rollback", default)).Jobs);
    }

    [Fact]
    public async Task InvalidDiscoveryAndChangedScopeCannotAdmitJobs()
    {
        var invalid = await SaveFeedAsync([], DiscoveryStatus.Invalid);
        await Assert.ThrowsAsync<ArgumentException>(() => jobs.AdmitAsync(Source(), invalid, "invalid", default));
        var parsed = await SaveFeedAsync(["https://example.test/articles/one"]);
        await Assert.ThrowsAsync<ArgumentException>(() => jobs.AdmitAsync(Source(path: "/articles"), parsed, "scope", default));
        Assert.Empty(await jobs.ListAsync(100, default));
    }

    [Fact]
    public async Task HtmlDiscoveryIsAdmittedOnlyWhenConfiguredModeMatchesRetainedRequest()
    {
        var attemptId = await SaveDiscoveryAsync(["https://example.test/articles/html-story"], CollectionMode.Html);

        await Assert.ThrowsAsync<ArgumentException>(() => jobs.AdmitAsync(Source(), attemptId, "html-wrong-mode", default));

        var admitted = await jobs.AdmitAsync(Source(CollectionMode.Html), attemptId, "html-correct-mode", default);
        Assert.Equal("https://example.test/articles/html-story", Assert.Single(admitted.Jobs).Url);
    }

    [Fact]
    public async Task ASourceListingThatLinksToItselfAdmitsOnlyItsArticlesBecauseAnArticleJobMustNeverBeARootCheck()
    {
        var template = Template("/articles");
        var configured = ConfiguredSource(template);
        string[] listing = [template.Url, "https://example.test/articles/story"];

        var manual = await jobs.AdmitAsync(configured, await SaveDiscoveryAsync(listing, CollectionMode.Html, pathPrefix: "/articles"),
            "listing-manual", default);
        var automatic = await jobs.AdmitAutomaticCheckAsync(configured,
            await SaveDiscoveryAsync(listing, CollectionMode.Html, pathPrefix: "/articles"), "listing-automatic", default);

        var article = Assert.Single(manual.Jobs);
        Assert.Equal("https://example.test/articles/story", article.Url);
        Assert.False((await jobs.GetAsync(article.JobId, default))!.Definition.IsRoot());
        Assert.Equal(1, manual.DuplicateCount);
        Assert.Empty(automatic.Jobs);
        Assert.Equal(2, automatic.DuplicateCount);
    }

    [Fact]
    public async Task AnIndexPageOfTheSourceIsNotAStatementSoOnlyTheArticlesOfTheListingAreAdmitted()
    {
        var configured = ConfiguredSource(Template("/articles"));
        string[] listing =
        [
            "https://example.test/articles", "https://example.test/articles/",
            "https://example.test/articles/template?DocumentTypeID=1951", "https://example.test/articles/story"
        ];

        var manual = await jobs.AdmitAsync(configured, await SaveDiscoveryAsync(listing, CollectionMode.Html, pathPrefix: "/articles"),
            "index-manual", default);

        Assert.Equal("https://example.test/articles/story", Assert.Single(manual.Jobs).Url);
        Assert.Equal(3, manual.DuplicateCount);
    }

    private async Task<string> SaveFeedAsync(string[] urls, DiscoveryStatus status = DiscoveryStatus.Parsed)
        => await SaveDiscoveryAsync(urls, CollectionMode.Feed, status);

    private async Task<string> SaveDiscoveryAsync(string[] urls, CollectionMode mode,
        DiscoveryStatus status = DiscoveryStatus.Parsed, string pathPrefix = "/")
    {
        var attemptId = "feed-" + Interlocked.Increment(ref feedSequence);
        var request = new CollectionRequest
        {
            JobId = "feed-job-" + feedSequence,
            SourceId = "source",
            Url = "https://example.test" + pathPrefix.TrimEnd('/') + "/feed.xml",
            AllowedOrigin = "https://example.test",
            AllowedPathPrefix = pathPrefix,
            ArtifactDirectory = artifacts,
            Mode = mode,
            MinDelayMilliseconds = 0
        };
        var result = new CollectionResult
        {
            JobId = request.JobId,
            SourceId = request.SourceId,
            RequestedUrl = request.Url,
            FinalUrl = request.Url,
            Outcome = CollectionOutcome.Captured,
            ObservedAt = DateTimeOffset.UtcNow,
            Response = new HttpResponseMetadata { StatusCode = 200, ContentType = "application/rss+xml", ContentEncodings = [] },
            SentValidators = null,
            BytesReceived = 10,
            RobotsRequestCount = 1,
            RequestCount = 2,
            Capture = new CaptureArtifact { Sha256 = new string('a', 64), RelativePath = new string('a', 64) + ".gz", ByteLength = 10 },
            Discovery = new DiscoveryResult
            {
                Status = status,
                Urls = urls,
                OutOfScopeCount = 0
            }
        };
        await CollectionAttemptImporter.ImportAsync(attemptId, request, result, attempts, default);
        return attemptId;
    }

    [Theory]
    [InlineData(CollectionMode.Feed)]
    [InlineData(CollectionMode.Html)]
    public async Task AdmissionPinsRevisionsAndRejectsCorrectedConfigurationOnReplay(CollectionMode mode)
    {
        var configuration = new CollectionConfiguration
        {
            People = [new PersonConfiguration { Id = "person", Name = "Original" }],
            Sources = [new WatchedSourceConfiguration
            {
                Id = "source", Coverage = [new SourceCoverageConfiguration { PersonId = "person" }],
                Url = "https://example.test/articles/template", AllowedOrigin = "https://example.test", AllowedPathPrefix = "/",
                Mode = mode, MaxRequests = 2, MaxBytes = 10, TimeoutSeconds = 4, MinDelayMilliseconds = 0,
                JobPolicy = new CollectionJobPolicy { MaxAttempts = 2, InitialRetryDelaySeconds = 0, MaximumRetryDelaySeconds = 0 }
            }]
        };
        var coverageDate = new DateOnly(2026, 7, 1);
        var original = new ConfiguredCollectionSource(configuration, "source", coverageDate);
        var nextAttempt = await SaveDiscoveryAsync(["https://example.test/articles/two"], mode);
        var admitted = await jobs.AdmitAsync(original, nextAttempt, "new-revision", default);
        var stored = await jobs.GetAsync(Assert.Single(admitted.Jobs).JobId, default);
        Assert.Equal(original.Revision, stored!.Definition.ConfigurationRevision);
        Assert.Equal(coverageDate, stored.Definition.CoverageAsOf);
        stored.Definition.Validate();
        var otherMode = mode == CollectionMode.Feed ? CollectionMode.Html : CollectionMode.Feed;
        var otherModeSource = new ConfiguredCollectionSource(
            configuration with { Sources = [configuration.Sources[0] with { Mode = otherMode }] }, "source", coverageDate);
        configuration.People[0] = configuration.People[0] with { Name = "Correction" };
        var corrected = new ConfiguredCollectionSource(configuration, "source", coverageDate);
        await Assert.ThrowsAsync<ArgumentException>(() => jobs.AdmitAsync(corrected, nextAttempt, "new-revision", default));
        Assert.Equal(admitted.Jobs, (await jobs.AdmitAsync(original, nextAttempt, "new-revision", default)).Jobs);
        await Assert.ThrowsAsync<ArgumentException>(() => jobs.AdmitAsync(otherModeSource, nextAttempt, "new-revision", default));

        var concurrentAttempt = await SaveDiscoveryAsync(["https://example.test/articles/concurrent"], mode);
        var configuredSource = new ConfiguredCollectionSource(configuration, "source");
        var concurrent = await Task.WhenAll(
            jobs.AdmitAsync(configuredSource, concurrentAttempt, "concurrent-configured", default),
            jobs.AdmitAsync(configuredSource, concurrentAttempt, "concurrent-configured", default));
        Assert.Single(concurrent[0].Jobs);
        Assert.Equal(concurrent[0].Jobs, concurrent[1].Jobs);

        var expiredConfiguration = configuration with
        {
            Version = 1,
            Sources = [configuration.Sources[0] with
            {
                Enabled = false,
                Coverage = [new SourceCoverageConfiguration { PersonId = "person", EndsBefore = new DateOnly(2021, 1, 1) }]
            }]
        };
        var expired = new ConfiguredCollectionSource(expiredConfiguration, "source");
        await Assert.ThrowsAsync<ArgumentException>(() => jobs.AdmitAsync(expired, nextAttempt, "new-expired", default));
        await Assert.ThrowsAsync<ArgumentException>(() => jobs.AdmitAsync(expired, "different-attempt", "new-revision", default));
    }

    private static ConfiguredCollectionSource ConfiguredSource(CollectionJobDefinition template) => new(
        new CollectionConfiguration
        {
            People = [new PersonConfiguration { Id = "person", Name = "Official" }],
            Sources = [new WatchedSourceConfiguration
            {
                Id = "source", Coverage = [new SourceCoverageConfiguration { PersonId = "person" }], Url = template.Url,
                AllowedOrigin = template.AllowedOrigin, AllowedPathPrefix = template.AllowedPathPrefix,
                Mode = CollectionMode.Html, MaxRequests = template.MaxRequests, MaxBytes = template.MaxBytes,
                TimeoutSeconds = template.TimeoutSeconds, MinDelayMilliseconds = template.MinDelayMilliseconds,
                JobPolicy = template.Policy
            }]
        }, "source", new DateOnly(2026, 10, 8));

    private static ConfiguredCollectionSource Source(CollectionMode mode = CollectionMode.Feed, string path = "/",
        DiscoveryAdmissionPolicy? policy = null, long maxBytes = 10) => new(
        new CollectionConfiguration
        {
            People = [new PersonConfiguration { Id = "person", Name = "Official" }],
            Sources = [new WatchedSourceConfiguration
            {
                Id = "source", Coverage = [new SourceCoverageConfiguration { PersonId = "person" }],
                Url = "https://example.test/articles/template", AllowedOrigin = "https://example.test", AllowedPathPrefix = path,
                Mode = mode, MaxRequests = 2, MaxBytes = maxBytes, TimeoutSeconds = 4, MinDelayMilliseconds = 0,
                JobPolicy = new CollectionJobPolicy { MaxAttempts = 2, InitialRetryDelaySeconds = 0, MaximumRetryDelaySeconds = 0 },
                AdmissionPolicy = policy
            }]
        }, "source", new DateOnly(2026, 7, 1));

    private static CollectionJobDefinition Template(string path = "/") =>
        CollectionFixtures.Definition(new CollectionConfiguration
        {
            People = [new PersonConfiguration { Id = "person", Name = "Official" }],
            Sources = [new WatchedSourceConfiguration
            {
                Id = "source", Coverage = [new SourceCoverageConfiguration { PersonId = "person" }],
                Url = "https://example.test/articles/template", AllowedOrigin = "https://example.test", AllowedPathPrefix = path,
                Mode = CollectionMode.Feed, MaxRequests = 2, MaxBytes = 10, TimeoutSeconds = 4, MinDelayMilliseconds = 0,
                JobPolicy = new CollectionJobPolicy { MaxAttempts = 2, InitialRetryDelaySeconds = 0, MaximumRetryDelaySeconds = 0 }
            }]
        }, "source", new DateOnly(2026, 7, 1)) with
        { Mode = CollectionMode.Page, ETag = null, LastModified = null };
}
