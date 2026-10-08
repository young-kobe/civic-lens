using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;
using Testcontainers.PostgreSql;

namespace CivicLens.Tests.Infrastructure.Collection;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresCollectionAttemptStoreTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset ObservedAt = new DateTimeOffset(2026, 10, 1, 12, 0, 0, TimeSpan.Zero).AddTicks(7);
    private string schema = null!;
    private ContextFactory factory = null!;
    private PostgresCollectionAttemptStore store = null!;

    public async Task InitializeAsync()
    {
        schema = $"test_{Guid.NewGuid():N}";
        var admin = new NpgsqlConnectionStringBuilder(postgres.ConnectionString);
        await using (var connection = new NpgsqlConnection(admin.ConnectionString))
        {
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = $"CREATE SCHEMA {schema}";
            await command.ExecuteNonQueryAsync();
        }

        admin.SearchPath = schema;
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(admin.ConnectionString).Options;
        factory = new ContextFactory(options);
        store = new PostgresCollectionAttemptStore(factory);
        await store.MigrateAsync();
        await using var db = await factory.CreateDbContextAsync();
        Assert.Empty(await db.Database.GetPendingMigrationsAsync());
        Assert.Equal(db.Database.GetMigrations().Order(), (await db.Database.GetAppliedMigrationsAsync()).Order());
        Assert.False(db.Database.HasPendingModelChanges());
    }

    public async Task DisposeAsync()
    {
        var connectionString = postgres.ConnectionString;
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = connection.CreateCommand();
        command.CommandText = $"DROP SCHEMA IF EXISTS {schema} CASCADE";
        await command.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task RobotsDelaySurvivesImportAndCannotChangeOnReplay()
    {
        var request = Request("robots-delay");
        var receipt = Result(request.JobId, CollectionOutcome.Failed,
            Response: Metadata(404, null, null, null, []), FailureCode: CollectionFailureCode.HttpError) with
        {
            RequestCount = 2,
            RobotsCrawlDelayMilliseconds = 12_345
        };
        var import = CollectionAttemptImporter.CreateImport("robots-delay", request, receipt);
        await store.ImportAtomicallyAsync(import, default);
        var retained = await store.GetAsync("robots-delay", default);
        Assert.Equal(12_345, retained!.RobotsCrawlDelayMilliseconds);
        Assert.Equal(ImportDisposition.DuplicateAttempt, (await store.ImportAtomicallyAsync(import, default)).Disposition);
        foreach (var delay in new long?[] { null, 0, 12_346 })
        {
            var conflict = CollectionAttemptImporter.CreateImport("robots-delay", request,
                receipt with { RobotsCrawlDelayMilliseconds = delay });
            await Assert.ThrowsAsync<InvalidOperationException>(() => store.ImportAtomicallyAsync(conflict, default));
        }
    }

    [Fact]
    public async Task DiscoveryIsAtomicImmutableAndRejectsConflictingReplay()
    {
        var request = Request("feed") with { Mode = CollectionMode.Feed };
        var urls = new[] { "https://example.test/article" };
        var result = Result("feed", CollectionOutcome.Captured,
            Response: Metadata(200, null, null, "application/rss+xml", []),
            Capture: Artifact(new string('e', 64), 12)) with
        {
            Discovery = new DiscoveryResult { Status = DiscoveryStatus.Parsed, Urls = urls }
        };
        var import = CollectionAttemptImporter.CreateImport("feed", request, result);
        urls[0] = "https://example.test/mutated";
        await store.ImportAtomicallyAsync(import, CancellationToken.None);
        var retained = await store.GetAsync("feed", CancellationToken.None);
        Assert.Equal("https://example.test/article", Assert.Single(retained!.Discovery!.Urls));
        Assert.Equal(ImportDisposition.DuplicateAttempt,
            (await store.ImportAtomicallyAsync(import, CancellationToken.None)).Disposition);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.ImportAtomicallyAsync(
            CollectionAttemptImporter.CreateImport("feed", request, result), CancellationToken.None));

        await using var db = await factory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_discovery_insert() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN RAISE EXCEPTION 'injected discovery failure'; END $$;
            CREATE TRIGGER fail_discovery_insert BEFORE INSERT ON collection_discoveries
            FOR EACH ROW EXECUTE FUNCTION fail_discovery_insert();
            """);
        var other = result with { JobId = "feed-rollback", Capture = Artifact(new string('f', 64), 12) };
        await Assert.ThrowsAsync<DbUpdateException>(() => store.ImportAtomicallyAsync(
            CollectionAttemptImporter.CreateImport("feed-rollback", request with { JobId = "feed-rollback" }, other),
            CancellationToken.None));
        Assert.Null(await store.GetAsync("feed-rollback", CancellationToken.None));
        var hash = new string('f', 64);
        Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT count(*) AS \"Value\" FROM collection_captures WHERE sha256 = {hash}").SingleAsync());
    }

    [Fact]
    public async Task PersistsEveryOutcomeWithExactTicksEncodingsAndNullValues()
    {
        var capturedResponse = Metadata(200, "\"v1\"", ObservedAt.AddTicks(13), "text/html; charset=utf-8", ["br", "gzip", "x-unknown"]);
        var captured = Result("capture", CollectionOutcome.Captured, Response: capturedResponse,
            Capture: Artifact(new string('a', 64), 73));
        var capturedDecision = await ImportAsync("capture", captured);
        var storedCaptured = await ImportAsync("capture", captured);
        var capture = Assert.IsType<CapturedAttemptResult>(storedCaptured.AttemptResult);
        Assert.Equal(73, capture.Capture.ByteLength);
        Assert.Equal(ObservedAt, capture.ObservedAt);
        Assert.Equal(capturedResponse.LastModified, capture.Response.LastModified);
        Assert.Equal(new[] { "br", "gzip", "x-unknown" }, capture.Response.ContentEncodings);
        Assert.Equal(ImportDisposition.DuplicateAttempt, storedCaptured.Disposition);

        var notModified = Result("not-modified", CollectionOutcome.NotModified,
            Response: Metadata(304, null, null, null, []),
            SentValidators: new HttpRequestValidators { ETag = "\"v1\"" });
        var notModifiedDecision = await ImportAsync("not-modified", notModified, etag: "\"v1\"");
        Assert.IsType<NotModifiedAttemptResult>(notModifiedDecision.AttemptResult);

        var failed = Result("failed", CollectionOutcome.Failed, FailureCode: CollectionFailureCode.TransportError);
        var failedDecision = await ImportAsync("failed", failed);
        var failedStored = await ImportAsync("failed", failed);
        Assert.IsType<FailedAttemptResult>(failedStored.AttemptResult);
        Assert.Equal("TransportError", ((FailedAttemptResult)failedStored.AttemptResult).FailureCode);

        var deferred = Result("deferred", CollectionOutcome.Deferred,
            Response: Metadata(429, null, null, null, []), FailureCode: CollectionFailureCode.RateLimited,
            RetryAfterSeconds: 17);
        var deferredDecision = await ImportAsync("deferred", deferred);
        var deferredStored = await ImportAsync("deferred", deferred);
        var deferredAttempt = Assert.IsType<DeferredAttemptResult>(deferredStored.AttemptResult);
        Assert.Equal(TimeSpan.FromSeconds(17), deferredAttempt.RetryDelay);
        Assert.Equal(ImportDisposition.DuplicateAttempt, deferredStored.Disposition);
        Assert.Equal(ImportDisposition.NewAttempt, capturedDecision.Disposition);
        Assert.Equal(ImportDisposition.NewAttempt, notModifiedDecision.Disposition);
        Assert.Equal(ImportDisposition.NewAttempt, failedDecision.Disposition);
        Assert.Equal(ImportDisposition.NewAttempt, deferredDecision.Disposition);
    }

    [Fact]
    public async Task PersistsDuplicateAndConflictingReplayAcrossAdapterInstances()
    {
        var captured = Result("replay", CollectionOutcome.Captured, Response: Metadata(200, null, null, null, []),
            Capture: Artifact(new string('b', 64), 10));
        var first = await ImportAsync("replay", captured);
        var otherStore = new PostgresCollectionAttemptStore(factory);
        var second = await Handler(captured, otherStore).ExecuteAsync("replay", Request("replay"));
        Assert.Equal(ImportDisposition.NewAttempt, first.Disposition);
        Assert.Equal(ImportDisposition.DuplicateAttempt, second.Disposition);
        Assert.Equal(10, Assert.IsType<CapturedAttemptResult>(second.AttemptResult).Capture.ByteLength);

        var conflicting = captured with { FinalUrl = "https://example.test/other" };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(conflicting, otherStore)
            .ExecuteAsync("replay", Request("replay")));
        Assert.Equal(ImportDisposition.DuplicateAttempt,
            (await Handler(captured, store).ExecuteAsync("replay", Request("replay"))).Disposition);
    }

    [Fact]
    public async Task SerializesConcurrentSameAndDistinctAttemptsAndRejectsHashLengthConflict()
    {
        var identical = Result("same", CollectionOutcome.Captured, Response: Metadata(200, null, null, null, []),
            Capture: Artifact(new string('c', 64), 12));
        var same = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ => Handler(identical, store)
            .ExecuteAsync("same", Request("same"))));
        Assert.Single(same, result => result.Disposition == ImportDisposition.NewAttempt);
        Assert.Equal(5, same.Count(result => result.Disposition == ImportDisposition.DuplicateAttempt));

        var distinct = Enumerable.Range(0, 6).Select(index =>
        {
            var attemptId = $"distinct-{index}";
            var result = Result(attemptId, CollectionOutcome.Captured, Response: Metadata(200, null, null, null, []),
                Capture: Artifact(new string('d', 64), 12));
            return Handler(result, store).ExecuteAsync(attemptId, Request(attemptId));
        });
        Assert.All(await Task.WhenAll(distinct), decision => Assert.Equal(ImportDisposition.NewAttempt, decision.Disposition));

        var badLength = Result("bad-length", CollectionOutcome.Captured, Response: Metadata(200, null, null, null, []),
            Capture: Artifact(new string('d', 64), 13));
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(badLength, store)
            .ExecuteAsync("bad-length", Request("bad-length")));
        Assert.Equal(ImportDisposition.DuplicateAttempt,
            (await Handler(Result("distinct-0", CollectionOutcome.Captured, Response: Metadata(200, null, null, null, []),
                Capture: Artifact(new string('d', 64), 12)), store).ExecuteAsync("distinct-0", Request("distinct-0"))).Disposition);

        var firstConflict = Result("conflict", CollectionOutcome.Captured, Response: Metadata(200, null, null, null, []),
            Capture: Artifact(new string('1', 64), 3));
        var secondConflict = Result("conflict", CollectionOutcome.Captured, Response: Metadata(200, null, null, null, []),
            Capture: Artifact(new string('2', 64), 4));
        var races = await Task.WhenAll(TryImportAsync(firstConflict), TryImportAsync(secondConflict));
        Assert.Single(races, outcome => outcome.Decision is not null);
        Assert.Single(races, outcome => outcome.Error is InvalidOperationException);
        var winningResult = races.Single(outcome => outcome.Decision is not null).Decision!.AttemptResult;
        var winnerReplay = await Handler(winningResult is CapturedAttemptResult win
            ? Result("conflict", CollectionOutcome.Captured, Response: Metadata(200, null, null, null, []),
                Capture: Artifact(win.Capture.Sha256, win.Capture.ByteLength))
            : throw new InvalidOperationException(), store).ExecuteAsync("conflict", Request("conflict"));
        Assert.Equal(ImportDisposition.DuplicateAttempt, winnerReplay.Disposition);
    }

    [Fact]
    public async Task LinksUnambiguous304AndPreservesUnresolvedAmbiguityOnReplay()
    {
        var prior = Result("prior", CollectionOutcome.Captured, Response: Metadata(200, "\"old\"", null, null, []),
            Capture: Artifact(new string('e', 64), 8));
        await ImportAsync("prior", prior);
        var unchanged = Result("304-one", CollectionOutcome.NotModified,
            Response: Metadata(304, "\"old\"", null, null, []),
            SentValidators: new HttpRequestValidators { ETag = "\"old\"" });
        var decision = await ImportAsync("304-one", unchanged, etag: "\"old\"");
        Assert.Equal(PriorCaptureLinkStatus.Linked, decision.PriorCaptureLinkStatus);
        Assert.Equal("prior", decision.PriorCapturedAttempt!.AttemptId);

        await ImportAsync("prior-two", prior with { JobId = "prior-two", ObservedAt = ObservedAt.AddTicks(-1), Capture = Artifact(new string('f', 64), 8) });
        var linkedReplay = await ImportAsync("304-one", unchanged, etag: "\"old\"");
        Assert.Equal(PriorCaptureLinkStatus.Linked, linkedReplay.PriorCaptureLinkStatus);
        Assert.Equal("prior", linkedReplay.PriorCapturedAttempt!.AttemptId);

        var ambiguous = Result("304-ambiguous", CollectionOutcome.NotModified, observedAt: ObservedAt.AddTicks(2),
            Response: Metadata(304, null, null, null, []), SentValidators: new HttpRequestValidators { ETag = "\"old\"" });
        var first = await ImportAsync("304-ambiguous", ambiguous, etag: "\"old\"");
        var replay = await ImportAsync("304-ambiguous", ambiguous, etag: "\"old\"");
        Assert.Equal(PriorCaptureLinkStatus.Unresolved, first.PriorCaptureLinkStatus);
        Assert.Equal(PriorCaptureLinkStatus.Unresolved, replay.PriorCaptureLinkStatus);
        Assert.Equal(ImportDisposition.DuplicateAttempt, replay.Disposition);
        Assert.Null(replay.PriorCapturedAttempt);

        await ImportAsync("prior-late", prior with
        {
            JobId = "prior-late",
            ObservedAt = ObservedAt.AddTicks(-1),
            Capture = Artifact(new string('7', 64), 8)
        });
        var unresolvedReplayAfterEvidence = await ImportAsync("304-ambiguous", ambiguous, etag: "\"old\"");
        Assert.Equal(PriorCaptureLinkStatus.Unresolved, unresolvedReplayAfterEvidence.PriorCaptureLinkStatus);
        Assert.Null(unresolvedReplayAfterEvidence.PriorCapturedAttempt);

        await using var db = await factory.CreateDbContextAsync();
        await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE collection_attempts SET prior_capture_sha256 = {new string('f', 64)} WHERE attempt_id = {"304-one"}"));
        var malformed = await Assert.ThrowsAsync<PostgresException>(() => db.Database.ExecuteSqlInterpolatedAsync(
            $"UPDATE collection_attempts SET response_status_code = NULL WHERE attempt_id = {"304-one"}"));
        Assert.Equal(PostgresErrorCodes.CheckViolation, malformed.SqlState);
    }

    [Fact]
    public async Task Unresolved304ReplayDoesNotGainAnEligibleCaptureImportedLater()
    {
        var unchanged = Result("unchanged", CollectionOutcome.NotModified,
            Response: Metadata(304, null, null, null, []),
            SentValidators: new HttpRequestValidators { ETag = "\"late\"" });
        var first = await ImportAsync("unchanged", unchanged, etag: "\"late\"");
        Assert.Equal(PriorCaptureLinkStatus.Unresolved, first.PriorCaptureLinkStatus);

        await ImportAsync("late-capture", Result("late-capture", CollectionOutcome.Captured,
            Response: Metadata(200, "\"late\"", null, null, []), Capture: Artifact(new string('7', 64), 8)));

        var replay = await ImportAsync("unchanged", unchanged, etag: "\"late\"");
        Assert.Equal(ImportDisposition.DuplicateAttempt, replay.Disposition);
        Assert.Equal(PriorCaptureLinkStatus.Unresolved, replay.PriorCaptureLinkStatus);
        Assert.Null(replay.PriorCapturedAttempt);
    }

    [Fact]
    public async Task CancellationWhileWaitingForLockAndFailedTransactionLeaveNoPartialRows()
    {
        await using var blocker = await factory.CreateDbContextAsync();
        await using var blockerTransaction = await blocker.Database.BeginTransactionAsync();
        await blocker.Database.ExecuteSqlRawAsync("SELECT pg_advisory_xact_lock(hashtextextended('civic-lens-collection-import', 0))");
        using var cancellation = new CancellationTokenSource();
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(10));
        var waiting = Handler(Result("cancelled", CollectionOutcome.Failed, FailureCode: CollectionFailureCode.TransportError), store)
            .ExecuteAsync("cancelled", Request("cancelled"), cancellation.Token);
        await using (var observer = await factory.CreateDbContextAsync())
        {
            while (!await observer.Database.SqlQueryRaw<bool>("""
                SELECT EXISTS (SELECT 1 FROM pg_locks WHERE locktype = 'advisory' AND NOT granted) AS "Value"
                """).SingleAsync(deadline.Token))
                await Task.Delay(10, deadline.Token);
        }
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => waiting.WaitAsync(TimeSpan.FromSeconds(10)));
        await blockerTransaction.RollbackAsync();
        var afterCancellation = await Handler(Result("cancelled", CollectionOutcome.Failed,
            FailureCode: CollectionFailureCode.TransportError), store).ExecuteAsync("cancelled", Request("cancelled"));
        Assert.Equal(ImportDisposition.NewAttempt, afterCancellation.Disposition);

        await using var db = await factory.CreateDbContextAsync();
        await db.Database.ExecuteSqlRawAsync("""
            CREATE FUNCTION fail_attempt_insert() RETURNS trigger LANGUAGE plpgsql AS $$
            BEGIN
                IF NEW.attempt_id = 'rollback-capture' THEN RAISE EXCEPTION 'injected persistence failure'; END IF;
                RETURN NEW;
            END $$;
            CREATE TRIGGER fail_attempt_insert BEFORE INSERT ON collection_attempts
            FOR EACH ROW EXECUTE FUNCTION fail_attempt_insert();
            """);
        var rollbackCapture = Result("rollback-capture", CollectionOutcome.Captured,
            Response: Metadata(200, null, null, null, []), Capture: Artifact(new string('9', 64), 5));
        await Assert.ThrowsAsync<DbUpdateException>(() => Handler(rollbackCapture, store)
            .ExecuteAsync("rollback-capture", Request("rollback-capture")));
        var rollbackHash = new string('9', 64);
        Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT count(*) AS \"Value\" FROM collection_captures WHERE sha256 = {rollbackHash}").SingleAsync());
        Assert.Equal(0, await db.Database.SqlQuery<int>($"SELECT count(*) AS \"Value\" FROM collection_attempts WHERE attempt_id = {"rollback-capture"}").SingleAsync());
    }

    private Task<CollectionImportDecision> ImportAsync(string id, CollectionResult result, string? etag = null) =>
        Handler(result, store).ExecuteAsync(id, Request(id, etag));

    private async Task<(CollectionImportDecision? Decision, Exception? Error)> TryImportAsync(CollectionResult result)
    {
        try
        {
            return (await Handler(result, store).ExecuteAsync("conflict", Request("conflict")), null);
        }
        catch (Exception error)
        {
            return (null, error);
        }
    }

    private static AttemptRunner Handler(CollectionResult result, ICollectionAttemptStore target) =>
        new(new CollectAndImportCollectionAttempt(new FakeCollector(result), target, new TestHandoffs()));

    private sealed class AttemptRunner(CollectAndImportCollectionAttempt handler)
    {
        public async Task<CollectionImportDecision> ExecuteAsync(string attemptId, CollectionRequest request,
            CancellationToken cancellationToken = default) =>
            (await handler.ExecuteAsync(attemptId, request, cancellationToken)).Decision;
    }

    private sealed class TestHandoffs : ICollectionReceiptHandoffStore
    {
        private readonly Dictionary<string, PendingCollectionHandoff> entries = [];
        public ValueTask<IAsyncDisposable> AcquireRecoveryLeaseAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IAsyncDisposable>(new NoopLease());
        public Task SaveAsync(PendingCollectionHandoff handoff, CancellationToken cancellationToken)
        {
            handoff.Validate();
            if (!entries.TryAdd(handoff.AttemptId, handoff)) throw new IOException("duplicate handoff");
            return Task.CompletedTask;
        }
        public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(entries.Keys.Order(StringComparer.Ordinal).ToArray());
        public Task<PendingCollectionHandoff> LoadAsync(string handoffId, CancellationToken cancellationToken) =>
            Task.FromResult(entries[handoffId]);
        public Task DeleteAsync(string handoffId, CancellationToken cancellationToken)
        {
            entries.Remove(handoffId);
            return Task.CompletedTask;
        }
        private sealed class NoopLease : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private static CollectionRequest Request(string id, string? etag = null) => new()
    {
        JobId = id,
        SourceId = "source",
        Url = "https://example.test/page",
        AllowedOrigin = "https://example.test",
        AllowedPathPrefix = "/",
        ArtifactDirectory = Path.GetTempPath(),
        MinDelayMilliseconds = 0,
        ETag = etag
    };

    private static CollectionResult Result(string jobId, CollectionOutcome outcome, HttpResponseMetadata? Response = null,
        CaptureArtifact? Capture = null, CollectionFailureCode? FailureCode = null, long? RetryAfterSeconds = null,
        HttpRequestValidators? SentValidators = null, DateTimeOffset? observedAt = null) => new()
        {
            JobId = jobId,
            SourceId = "source",
            RequestedUrl = "https://example.test/page",
            FinalUrl = "https://example.test/page",
            Outcome = outcome,
            ObservedAt = observedAt ?? ObservedAt,
            Response = Response,
            SentValidators = SentValidators,
            BytesReceived = outcome == CollectionOutcome.Captured ? Capture!.ByteLength : 0,
            RobotsRequestCount = outcome == CollectionOutcome.Failed && Response is null ? 0 : 1,
            RequestCount = outcome == CollectionOutcome.Failed && Response is null ? 0 : 2,
            Capture = Capture,
            FailureCode = FailureCode,
            RetryAfterSeconds = RetryAfterSeconds
        };

    private static HttpResponseMetadata Metadata(int status, string? etag, DateTimeOffset? modified, string? type,
        string[] encodings) => new() { StatusCode = status, ETag = etag, LastModified = modified, ContentType = type, ContentEncodings = encodings };

    private static CaptureArtifact Artifact(string hash, long length) => new() { Sha256 = hash, RelativePath = $"{hash}.gz", ByteLength = length };

    private sealed class FakeCollector(CollectionResult result) : ICollectorProcess
    {
        public Task<CollectionResult> RunAsync(CollectionRequest request, CancellationToken cancellationToken) => Task.FromResult(result);
    }

    private sealed class ContextFactory(DbContextOptions<CollectionAttemptDbContext> options) : IDbContextFactory<CollectionAttemptDbContext>
    {
        public CollectionAttemptDbContext CreateDbContext() => new(options);
        public Task<CollectionAttemptDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(CreateDbContext());
    }
}

[CollectionDefinition(PostgresCollection.Name, DisableParallelization = true)]
public sealed class PostgresCollectionDefinition : ICollectionFixture<PostgresCollection>
{
}

public sealed class PostgresCollection : IAsyncLifetime
{
    public const string Name = "Postgres collection persistence";
    private readonly PostgreSqlContainer container = new PostgreSqlBuilder(
        "postgres:18.3-alpine@sha256:54451ecb8ab38c24c3ec123f2fd501303a3a1856a5c66e98cecf2460d5e1e9d7")
        .WithDatabase("civic_lens")
        .WithUsername("civic_lens")
        .WithPassword("civic_lens")
        .Build();

    public string ConnectionString => container.GetConnectionString();
    public Task InitializeAsync() => container.StartAsync();
    public Task DisposeAsync() => container.DisposeAsync().AsTask();
}
