using CivicLens.Application.Collection;
using CivicLens.Application.Collection.Discovery;
using CivicLens.Application.Collection.Jobs;
using CivicLens.Collection.Contracts;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Jobs;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.Discovery;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresFeedAdmissionTests(PostgresCollection postgres) : IAsyncLifetime
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
        var request = new FeedAdmissionRequest(attemptId, "batch-one", Template(),
            new FeedAdmissionPolicy { MaxJobs = 10, MaxTotalRequests = requests, MaxTotalBytes = bytes, MaxTotalTimeoutSeconds = seconds });

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
        var duplicate = await jobs.AdmitAsync(new FeedAdmissionRequest(sameCandidate, "batch-two", Template(), new FeedAdmissionPolicy()), default);
        Assert.Equal(0, duplicate.AdmittedCount);
        Assert.Equal(1, duplicate.DuplicateCount);

        await Assert.ThrowsAsync<ArgumentException>(() => jobs.AdmitAsync(request with
        {
            ArticleTemplate = Template() with { MaxBytes = 11 }
        }, default));
    }

    [Fact]
    public async Task ConcurrentFeedAttemptsCreateOnlyOneJobForAnExactSourceUrlPair()
    {
        var url = "https://example.test/articles/shared";
        var firstAttempt = await SaveFeedAsync([url]);
        var secondAttempt = await SaveFeedAsync([url]);

        var results = await Task.WhenAll(
            jobs.AdmitAsync(new FeedAdmissionRequest(firstAttempt, "concurrent-one", Template(), new FeedAdmissionPolicy()), default),
            jobs.AdmitAsync(new FeedAdmissionRequest(secondAttempt, "concurrent-two", Template(), new FeedAdmissionPolicy()), default));

        Assert.Equal(1, results.Sum(result => result.AdmittedCount));
        Assert.Equal(1, results.Sum(result => result.DuplicateCount));
        Assert.Single(results.SelectMany(result => result.Jobs).Select(job => job.JobId).Distinct());
    }

    [Fact]
    public async Task FailedAdmissionRollsBackJobsBindingsAndBatchAndCanRetry()
    {
        var attemptId = await SaveFeedAsync(["https://example.test/articles/one"]);
        var request = new FeedAdmissionRequest(attemptId, "rollback", Template(), new FeedAdmissionPolicy());
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using (var trigger = new NpgsqlCommand("""
            CREATE FUNCTION fail_admission() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'injected admission failure'; END $$;
            CREATE TRIGGER fail_admission BEFORE INSERT ON collection_feed_admission_batches
            FOR EACH ROW EXECUTE FUNCTION fail_admission();
            """, connection))
            await trigger.ExecuteNonQueryAsync();
        await Assert.ThrowsAsync<Microsoft.EntityFrameworkCore.DbUpdateException>(() => jobs.AdmitAsync(request, default));
        foreach (var table in new[] { "collection_jobs", "collection_feed_candidate_jobs", "collection_feed_admission_batches" })
        {
            await using var count = new NpgsqlCommand($"SELECT count(*) FROM {table}", connection);
            Assert.Equal(0L, await count.ExecuteScalarAsync());
        }
        await using (var drop = new NpgsqlCommand("DROP TRIGGER fail_admission ON collection_feed_admission_batches", connection))
            await drop.ExecuteNonQueryAsync();
        Assert.Single((await jobs.AdmitAsync(request, default)).Jobs);
    }

    [Fact]
    public async Task InvalidDiscoveryAndChangedScopeCannotAdmitJobs()
    {
        var invalid = await SaveFeedAsync([], FeedDiscoveryStatus.Invalid);
        await Assert.ThrowsAsync<ArgumentException>(() => jobs.AdmitAsync(
            new FeedAdmissionRequest(invalid, "invalid", Template(), new FeedAdmissionPolicy()), default));
        var parsed = await SaveFeedAsync(["https://example.test/articles/one"]);
        await Assert.ThrowsAsync<ArgumentException>(() => jobs.AdmitAsync(
            new FeedAdmissionRequest(parsed, "scope", Template("/articles"), new FeedAdmissionPolicy()), default));
        Assert.Empty(await jobs.ListAsync(100, default));
    }

    private async Task<string> SaveFeedAsync(string[] urls, FeedDiscoveryStatus status = FeedDiscoveryStatus.Parsed)
    {
        var attemptId = "feed-" + Interlocked.Increment(ref feedSequence);
        var request = new CollectionRequest
        {
            JobId = "feed-job-" + feedSequence,
            SourceId = "source",
            Url = "https://example.test/feed.xml",
            AllowedOrigin = "https://example.test",
            AllowedPathPrefix = "/",
            ArtifactDirectory = artifacts,
            Mode = CollectionMode.Feed,
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
            RequestCount = 2,
            Capture = new CaptureArtifact { Sha256 = new string('a', 64), RelativePath = new string('a', 64) + ".gz", ByteLength = 10 },
            Discovery = new FeedDiscoveryResult { Status = status, Urls = urls }
        };
        await CollectionAttemptImporter.ImportAsync(attemptId, request, result, attempts, default);
        return attemptId;
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
