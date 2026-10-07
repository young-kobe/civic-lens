using System.Text.Json;
using System.Text;
using System.Security.Cryptography;
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
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.Discovery;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresDiscoveryAdmissionTests(PostgresCollection postgres) : IAsyncLifetime
{
    private readonly string schema = "admission_" + Guid.NewGuid().ToString("N");
    private readonly string artifacts = Path.Combine(Path.GetTempPath(), "civic-admission-" + Guid.NewGuid().ToString("N"));
    private PostgresCollectionJobStore jobs = null!;
    private PostgresCollectionAttemptStore attempts = null!;
    private int feedSequence;
    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"CREATE SCHEMA {schema}", connection);
        await command.ExecuteNonQueryAsync();
        connectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString) { SearchPath = schema }.ConnectionString;
        jobs = PostgresCollectionJobStore.FromConnectionString(connectionString);
        attempts = PostgresCollectionAttemptStore.FromConnectionString(connectionString);
        await attempts.MigrateAsync();
        Directory.CreateDirectory(artifacts);
    }

    public async Task DisposeAsync()
    {
        Directory.Delete(artifacts, true);
        await using var connection = new NpgsqlConnection(postgres.ConnectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"DROP SCHEMA {schema} CASCADE", connection);
        await command.ExecuteNonQueryAsync();
    }

    [Theory]
    [InlineData(8, 100, 100)]
    [InlineData(100, 40, 100)]
    [InlineData(100, 100, 16)]
    public async Task AdmissionReplaysDeduplicatesAndReservesFullRetryBudgets(int requests, long bytes, int seconds)
    {
        var urls = new[] { "https://example.test/articles/one", "https://example.test/articles/two", "https://example.test/articles/three" };
        var attemptId = await SaveFeedAsync(urls);
        var request = new DiscoveryAdmissionRequest(attemptId, "batch-one", Template(),
            new DiscoveryAdmissionPolicy { MaxJobs = 10, MaxTotalRequests = requests, MaxTotalBytes = bytes, MaxTotalTimeoutSeconds = seconds });

        var first = await jobs.AdmitAsync(request, default);
        var replay = await jobs.AdmitAsync(request, default);

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
        var duplicate = await jobs.AdmitAsync(new DiscoveryAdmissionRequest(sameCandidate, "batch-two", Template(), new DiscoveryAdmissionPolicy()), default);
        Assert.Equal(0, duplicate.AdmittedCount);
        Assert.Equal(1, duplicate.DuplicateCount);

        await Assert.ThrowsAsync<ArgumentException>(() => jobs.AdmitAsync(request with
        {
            ArticleTemplate = Template() with { MaxBytes = 11 }
        }, default));
    }

    [Fact]
    public async Task AdmissionChecksExistingCandidatesWithOneQueryAndReplaysWithoutRechecking()
    {
        var urls = Enumerable.Range(0, 40)
            .Select(index => $"https://example.test/articles/{index:D2}")
            .ToArray();
        var firstAttempt = await SaveFeedAsync(urls.Take(10).ToArray());
        var firstBatch = await jobs.AdmitAsync(new DiscoveryAdmissionRequest(firstAttempt, "candidate-seed",
            Template(), new DiscoveryAdmissionPolicy()), default);
        Assert.Equal(10, firstBatch.AdmittedCount);

        var attempt = await SaveFeedAsync(urls);
        var counter = new CandidateSelectCounter();
        var countedFactory = new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString)
                .AddInterceptors(counter).Options);
        var countedJobs = new PostgresCollectionJobStore(countedFactory);
        var request = new DiscoveryAdmissionRequest(attempt, "candidate-mixed", Template(), new DiscoveryAdmissionPolicy { MaxJobs = 40 });

        var admitted = await countedJobs.AdmitAsync(request, default);
        Assert.Equal(10, admitted.DuplicateCount);
        Assert.Equal(30, admitted.AdmittedCount);
        Assert.Equal(urls.Skip(10), admitted.Jobs.Select(job => job.Url));
        Assert.Equal(1, counter.CandidateSelectCount);

        var replay = await countedJobs.AdmitAsync(request, default);
        Assert.Equal(admitted.Jobs, replay.Jobs);
        Assert.Equal(admitted.DeferredCount, replay.DeferredCount);
        Assert.Equal(admitted.DuplicateCount, replay.DuplicateCount);
        Assert.Equal(1, counter.CandidateSelectCount);
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
            jobs.AdmitAsync(new DiscoveryAdmissionRequest(firstAttempt, "concurrent-one", Template(), new DiscoveryAdmissionPolicy()), default),
            jobs.AdmitAsync(new DiscoveryAdmissionRequest(secondAttempt, "concurrent-two", Template(), new DiscoveryAdmissionPolicy(), CollectionMode.Html), default));

        Assert.Equal(1, results.Sum(result => result.AdmittedCount));
        Assert.Equal(1, results.Sum(result => result.DuplicateCount));
        Assert.Single(results.SelectMany(result => result.Jobs).Select(job => job.JobId).Distinct());
    }

    [Fact]
    public async Task FailedAdmissionRollsBackJobsBindingsAndBatchAndCanRetry()
    {
        var attemptId = await SaveFeedAsync(["https://example.test/articles/one"]);
        var request = new DiscoveryAdmissionRequest(attemptId, "rollback", Template(), new DiscoveryAdmissionPolicy());
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var trigger = new NpgsqlCommand("""
            CREATE FUNCTION fail_admission() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'injected admission failure'; END $$;
            CREATE TRIGGER fail_admission BEFORE INSERT ON collection_admission_batches
            FOR EACH ROW EXECUTE FUNCTION fail_admission();
            """, connection))
            await trigger.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() => jobs.AdmitAsync(request, default));
        foreach (var table in new[] { "collection_jobs", "collection_candidate_jobs", "collection_admission_batches" })
        {
            await using var count = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection);
            Assert.Equal(0L, await count.ExecuteScalarAsync());
        }
        await using (var drop = new NpgsqlCommand("DROP TRIGGER fail_admission ON collection_admission_batches", connection))
            await drop.ExecuteNonQueryAsync();
        Assert.Single((await jobs.AdmitAsync(request, default)).Jobs);
    }

    [Fact]
    public async Task InvalidDiscoveryAndChangedScopeCannotAdmitJobs()
    {
        var invalid = await SaveFeedAsync([], DiscoveryStatus.Invalid);
        await Assert.ThrowsAsync<ArgumentException>(() => jobs.AdmitAsync(
            new DiscoveryAdmissionRequest(invalid, "invalid", Template(), new DiscoveryAdmissionPolicy()), default));
        var parsed = await SaveFeedAsync(["https://example.test/articles/one"]);
        await Assert.ThrowsAsync<ArgumentException>(() => jobs.AdmitAsync(
            new DiscoveryAdmissionRequest(parsed, "scope", Template("/articles"), new DiscoveryAdmissionPolicy()), default));
        Assert.Empty(await jobs.ListAsync(100, default));
    }

    [Fact]
    public async Task HtmlDiscoveryIsAdmittedOnlyWhenConfiguredModeMatchesRetainedRequest()
    {
        var attemptId = await SaveDiscoveryAsync(["https://example.test/articles/html-story"], CollectionMode.Html);

        await Assert.ThrowsAsync<ArgumentException>(() => jobs.AdmitAsync(new DiscoveryAdmissionRequest(attemptId,
            "html-wrong-mode", Template(), new DiscoveryAdmissionPolicy()), default));

        var admitted = await jobs.AdmitAsync(new DiscoveryAdmissionRequest(attemptId, "html-correct-mode", Template(),
            new DiscoveryAdmissionPolicy(), CollectionMode.Html), default);
        Assert.Equal("https://example.test/articles/html-story", Assert.Single(admitted.Jobs).Url);
    }

    [Fact]
    public async Task VersionFourFeedDiscoveryAndAdmissionReplayRemainSupported()
    {
        var attemptId = await SaveDiscoveryAsync(["https://example.test/articles/v4"], CollectionMode.Feed,
            version: CollectionProtocol.PreviousVersion);
        var request = new DiscoveryAdmissionRequest(attemptId, "version-four-replay", Template(),
            new DiscoveryAdmissionPolicy());

        var first = await jobs.AdmitAsync(request, default);
        var replay = await jobs.AdmitAsync(request, default);

        Assert.Equal(first.Jobs, replay.Jobs);
        Assert.Single(first.Jobs);
    }

    [Fact]
    public async Task GeneralizationMigrationPreservesVersionFourDiscoveryAndAdmissionRows()
    {
        var upgradeSchema = "upgrade_" + Guid.NewGuid().ToString("N");
        await using (var connection = new NpgsqlConnection(postgres.ConnectionString))
        {
            await connection.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE SCHEMA {upgradeSchema}", connection);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var upgradeConnectionString = new NpgsqlConnectionStringBuilder(postgres.ConnectionString)
            {
                SearchPath = upgradeSchema
            }.ConnectionString;
            var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(upgradeConnectionString).Options;
            await using var db = new CollectionAttemptDbContext(options);
            await db.GetService<IMigrator>().MigrateAsync("20261007002616_FeedDiscoveryAdmission");
            await SeedHistoricalFeedRowsAsync(upgradeConnectionString);

            await db.Database.MigrateAsync();

            await using var connection = new NpgsqlConnection(upgradeConnectionString);
            await connection.OpenAsync();
            Assert.Equal(1L, await CountAsync(connection, "collection_discoveries"));
            Assert.Equal(1L, await CountAsync(connection, "collection_candidate_jobs"));
            Assert.Equal(1L, await CountAsync(connection, "collection_admission_batches"));
            await using var read = new NpgsqlCommand(
                "SELECT request_json FROM collection_discoveries WHERE attempt_id = 'historical-v4'", connection);
            var requestJson = (string)(await read.ExecuteScalarAsync())!;
            Assert.Contains("\"version\":4", requestJson, StringComparison.Ordinal);
            Assert.False(await TableExistsAsync(connection, "collection_feed_discoveries"));
            var upgradedAttempts = PostgresCollectionAttemptStore.FromConnectionString(upgradeConnectionString);
            var evidence = await upgradedAttempts.GetAsync("historical-v4", default);
            Assert.Equal(4, evidence!.Discovery!.Request.Version);
            Assert.Equal("https://example.test/article", Assert.Single(evidence.Discovery.Urls));
            var upgradedJobs = PostgresCollectionJobStore.FromConnectionString(upgradeConnectionString);
            var replay = await upgradedJobs.AdmitAsync(new DiscoveryAdmissionRequest("historical-v4", "historical-batch",
                Template(), new DiscoveryAdmissionPolicy()), default);
            Assert.Equal("historical-job", Assert.Single(replay.Jobs).JobId);
            var duplicate = await upgradedJobs.AdmitAsync(new DiscoveryAdmissionRequest("historical-v4", "another-batch",
                Template(), new DiscoveryAdmissionPolicy()), default);
            Assert.Equal(1, duplicate.DuplicateCount);
            Assert.Empty(duplicate.Jobs);
            await db.GetService<IMigrator>().MigrateAsync("20261007002616_FeedDiscoveryAdmission");
            Assert.Equal(1L, await CountAsync(connection, "collection_feed_discoveries"));
            await db.Database.MigrateAsync();
            Assert.Equal(1L, await CountAsync(connection, "collection_discoveries"));
        }
        finally
        {
            await using var connection = new NpgsqlConnection(postgres.ConnectionString);
            await connection.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP SCHEMA {upgradeSchema} CASCADE", connection);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private static async Task SeedHistoricalFeedRowsAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var seed = new NpgsqlCommand("""
            INSERT INTO collection_captures (sha256, byte_length) VALUES (repeat('a', 64), 10);
            INSERT INTO collection_attempts (attempt_id, source_id, requested_url, final_url, observed_at_utc_ticks,
                outcome, has_response, has_sent_validators, response_status_code, response_content_encodings, capture_sha256)
            VALUES ('historical-v4', 'source', 'https://example.test/feed.xml', 'https://example.test/feed.xml',
                638953920000000000, 'captured', true, false, 200, ARRAY[]::text[], repeat('a', 64));
            INSERT INTO collection_feed_discoveries (attempt_id, source_id, request_json, discovery_json)
            VALUES ('historical-v4', 'source',
                '{"version":4,"jobId":"historical-job","sourceId":"source","url":"https://example.test/feed.xml","allowedOrigin":"https://example.test","allowedPathPrefix":"/","artifactDirectory":"/tmp/artifacts","mode":"feed","maxCandidates":100,"maxRequests":2,"maxBytes":10,"timeoutSeconds":4,"minDelayMilliseconds":0}',
                '{"status":"parsed","urls":["https://example.test/article"]}');
            INSERT INTO collection_jobs (job_id, idempotency_key, definition_json, state, created_at,
                cancellation_requested, charged_requests, charged_bytes, charged_seconds, lease_fence)
            VALUES ('historical-job', 'historical-job-key', @definition, 'Pending', 1, false, 0, 0, 0, 0);
            INSERT INTO collection_feed_candidate_jobs (candidate_hash, source_id, url, job_id)
            VALUES (@hash, 'source', 'https://example.test/article', 'historical-job');
            INSERT INTO collection_feed_admission_batches (idempotency_key, attempt_id, input_json, result_json, created_at)
            VALUES ('historical-batch', 'historical-v4',
                @input,
                '{"jobs":[{"jobId":"historical-job","url":"https://example.test/article"}],"deferredCount":0,"duplicateCount":0,"admittedCount":1}', 1);
            """, connection);
        seed.Parameters.AddWithValue("definition", JsonSerializer.Serialize(Template() with { Url = "https://example.test/article" }, CollectionProtocol.JsonOptions));
        // The v4 feed batch contract had exactly these three input fields in this order.
        seed.Parameters.AddWithValue("input", JsonSerializer.Serialize(new
        {
            attemptId = "historical-v4",
            articleTemplate = Template(),
            policy = new DiscoveryAdmissionPolicy()
        }, CollectionProtocol.JsonOptions));
        seed.Parameters.AddWithValue("hash", Convert.ToHexStringLower(SHA256.HashData(Encoding.UTF8.GetBytes("6:sourcehttps://example.test/article"))));
        await seed.ExecuteNonQueryAsync();
    }

    private static async Task<long> CountAsync(NpgsqlConnection connection, string table)
    {
        await using var command = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection);
        return (long)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<bool> TableExistsAsync(NpgsqlConnection connection, string table)
    {
        await using var command = new NpgsqlCommand("SELECT to_regclass(@name) IS NOT NULL", connection);
        command.Parameters.AddWithValue("name", table);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    private async Task<string> SaveFeedAsync(string[] urls, DiscoveryStatus status = DiscoveryStatus.Parsed)
        => await SaveDiscoveryAsync(urls, CollectionMode.Feed, status);

    private async Task<string> SaveDiscoveryAsync(string[] urls, CollectionMode mode,
        DiscoveryStatus status = DiscoveryStatus.Parsed, int version = CollectionProtocol.Version)
    {
        var attemptId = "feed-" + Interlocked.Increment(ref feedSequence);
        var request = new CollectionRequest
        {
            Version = version,
            JobId = "feed-job-" + feedSequence,
            SourceId = "source",
            Url = "https://example.test/feed.xml",
            AllowedOrigin = "https://example.test",
            AllowedPathPrefix = "/",
            ArtifactDirectory = artifacts,
            Mode = mode,
            MinDelayMilliseconds = 0
        };
        var result = new CollectionResult
        {
            Version = version,
            JobId = request.JobId,
            SourceId = request.SourceId,
            RequestedUrl = request.Url,
            FinalUrl = request.Url,
            Outcome = CollectionOutcome.Captured,
            ObservedAt = DateTimeOffset.UtcNow,
            Response = new HttpResponseMetadata { StatusCode = 200, ContentType = "application/rss+xml", ContentEncodings = [] },
            SentValidators = null,
            BytesReceived = 10,
            RobotsRequestCount = version >= 6 ? 1 : null,
            RequestCount = 2,
            Capture = new CaptureArtifact { Sha256 = new string('a', 64), RelativePath = new string('a', 64) + ".gz", ByteLength = 10 },
            Discovery = new DiscoveryResult { Status = status, Urls = urls }
        };
        await CollectionAttemptImporter.ImportAsync(attemptId, request, result, attempts, default);
        return attemptId;
    }

    [Theory]
    [InlineData(CollectionMode.Feed)]
    [InlineData(CollectionMode.Html)]
    public async Task AdmissionPinsRevisionsAndReplaysLegacyBatchesWithoutInventingHistory(CollectionMode mode)
    {
        var attempt = await SaveDiscoveryAsync(["https://example.test/articles/one"], mode);
        var template = Template();
        var configuration = new CollectionConfiguration
        {
            People = [new PersonConfiguration { Id = "person", Name = "Original" }],
            Sources = [new WatchedSourceConfiguration
            {
                Id = "source", PersonIds = ["person"], Url = template.Url,
                AllowedOrigin = template.AllowedOrigin, AllowedPathPrefix = template.AllowedPathPrefix,
                Mode = mode, MaxRequests = template.MaxRequests, MaxBytes = template.MaxBytes,
                TimeoutSeconds = template.TimeoutSeconds, MinDelayMilliseconds = template.MinDelayMilliseconds,
                JobPolicy = template.Policy
            }]
        };
        var definition = CollectionJobDefinition.FromConfiguration(configuration, "source", new DateOnly(2026, 7, 1)) with
        { Mode = CollectionMode.Page, ETag = null, LastModified = null };
        var legacyRequest = new DiscoveryAdmissionRequest(attempt, "legacy-revision", template, new DiscoveryAdmissionPolicy(), mode);
        var legacy = await jobs.AdmitAsync(legacyRequest, default);
        var replay = await jobs.AdmitAsync(legacyRequest with { ArticleTemplate = definition }, default);
        Assert.Equal(legacy.Jobs, replay.Jobs);
        Assert.Null((await jobs.GetAsync(Assert.Single(replay.Jobs).JobId, default))!.Definition.ConfigurationRevision);
        var otherMode = mode == CollectionMode.Feed ? CollectionMode.Html : CollectionMode.Feed;
        await Assert.ThrowsAsync<ArgumentException>(() => jobs.AdmitAsync(legacyRequest with
        { ExpectedDiscoveryMode = otherMode }, default));

        var nextAttempt = await SaveDiscoveryAsync(["https://example.test/articles/two"], mode);
        var request = new DiscoveryAdmissionRequest(nextAttempt, "new-revision", definition, new DiscoveryAdmissionPolicy(), mode);
        var admitted = await jobs.AdmitAsync(request, default);
        var stored = await jobs.GetAsync(Assert.Single(admitted.Jobs).JobId, default);
        Assert.Equal(definition.ConfigurationRevision, stored!.Definition.ConfigurationRevision);
        Assert.Equal(definition.CoverageAsOf, stored.Definition.CoverageAsOf);
        stored.Definition.Validate();
        configuration.People[0] = configuration.People[0] with { Name = "Correction" };
        var corrected = CollectionJobDefinition.FromConfiguration(configuration, "source", definition.CoverageAsOf) with
        { Mode = CollectionMode.Page, ETag = null, LastModified = null };
        await Assert.ThrowsAsync<ArgumentException>(() => jobs.AdmitAsync(request with { ArticleTemplate = corrected }, default));
        Assert.Equal(admitted.Jobs, (await jobs.AdmitAsync(request, default)).Jobs);

        var concurrentAttempt = await SaveDiscoveryAsync(["https://example.test/articles/concurrent"], mode);
        var configuredSource = new ConfiguredCollectionSource(configuration, "source");
        var concurrent = await Task.WhenAll(
            jobs.AdmitAsync(configuredSource, concurrentAttempt, "concurrent-configured", default),
            jobs.AdmitAsync(configuredSource, concurrentAttempt, "concurrent-configured", default));
        Assert.Single(concurrent[0].Jobs);
        Assert.Equal(concurrent[0].Jobs, concurrent[1].Jobs);

        var expiredConfiguration = configuration with
        {
            Version = 2,
            Sources = [configuration.Sources[0] with
            {
                PersonIds = null, Enabled = false,
                Coverage = [new SourceCoverageConfiguration { PersonId = "person", EndsBefore = new DateOnly(2021, 1, 1) }]
            }]
        };
        var expired = new ConfiguredCollectionSource(expiredConfiguration, "source");
        Assert.Equal(legacy.Jobs, (await jobs.AdmitAsync(expired, attempt, "legacy-revision", default)).Jobs);
        await Assert.ThrowsAsync<ArgumentException>(() => jobs.AdmitAsync(expired, attempt, "new-expired", default));
        await Assert.ThrowsAsync<ArgumentException>(() => jobs.AdmitAsync(expired, "different-attempt", "legacy-revision", default));
    }

    private static CollectionJobDefinition Template(string path = "/") => new()
    {
        SourceId = "source",
        Url = "https://example.test/articles/template",
        AllowedOrigin = "https://example.test",
        AllowedPathPrefix = path,
        MaxRequests = 2,
        MaxBytes = 10,
        TimeoutSeconds = 4,
        MinDelayMilliseconds = 0,
        Policy = new CollectionJobPolicy { MaxAttempts = 2, InitialRetryDelaySeconds = 0, MaximumRetryDelaySeconds = 0 }
    };
}
