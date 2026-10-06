using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;

namespace CivicLens.Tests.Application.Collection;

public sealed class CollectAndImportCollectionAttemptTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 5, 1, 12, 30, 0, TimeSpan.Zero);

    [Fact]
    public async Task MapsCapturedResultAndKeepsAttemptIdentityIndependentFromJobId()
    {
        var request = Request();
        var result = Result(CollectionOutcome.Captured, request) with
        {
            Response = Response(200, "\"v1\"", ["br", "gzip"]),
            Capture = Capture(),
            BytesReceived = 12,
            RequestCount = 2
        };
        var store = new FakeStore();

        await Handler(result, store).ExecuteAsync("attempt-owned-by-app", request);

        var mapped = Assert.IsType<CapturedAttemptResult>(store.Received!.AttemptResult);
        Assert.Equal("attempt-owned-by-app", mapped.AttemptId);
        Assert.NotEqual(request.JobId, mapped.AttemptId);
        Assert.Equal(request.SourceId, mapped.SourceId);
        Assert.Equal(request.Url, mapped.RequestedUrl);
        Assert.Equal(result.FinalUrl, mapped.FinalUrl);
        Assert.Equal(ObservedAt, mapped.ObservedAt);
        Assert.Equal(new CaptureIdentity(new string('a', 64), 12), mapped.Capture);
        Assert.Equal(new CollectionResponse(200, "\"v1\"", ObservedAt, "text/html; charset=utf-8", ["br", "gzip"]), mapped.Response);
        Assert.Null(store.Received.SentValidators);
    }

    [Fact]
    public async Task Maps304AndUsesValidatorsActuallySentForConservativePriorLink()
    {
        var request = Request() with { ETag = "\"v1\"", LastModified = ObservedAt };
        var result = Result(CollectionOutcome.NotModified, request) with
        {
            Response = Response(304, "\"v1\"", []),
            SentValidators = new HttpRequestValidators { ETag = request.ETag, LastModified = request.LastModified },
            RequestCount = 2
        };
        var prior = Captured("prior", request.SourceId, request.Url, result.FinalUrl);
        var store = new FakeStore { Prior = prior };

        var decision = await Handler(result, store).ExecuteAsync("attempt-304", request);

        var mapped = Assert.IsType<NotModifiedAttemptResult>(decision.AttemptResult);
        Assert.Equal("attempt-304", mapped.AttemptId);
        Assert.Equal(new SentValidators(request.ETag, request.LastModified), store.Received!.SentValidators);
        Assert.Equal(PriorCaptureLinkStatus.Linked, decision.PriorCaptureLinkStatus);
        Assert.Same(prior, decision.PriorCapturedAttempt);
        Assert.Null(mapped.Response.ContentType);
    }

    [Fact]
    public async Task RobotsFailureDoesNotRetainRequestedValidatorsAsSent()
    {
        var request = Request() with { ETag = "\"v1\"", LastModified = ObservedAt };
        var result = Result(CollectionOutcome.Failed, request) with { FailureCode = CollectionFailureCode.RobotsDenied };
        var store = new FakeStore();

        await Handler(result, store).ExecuteAsync("robots-failure", request);

        Assert.Null(store.Existing!.SentValidators);
    }

    [Fact]
    public async Task ImportUsesReceiptValidatorsAtWirePrecisionFor304Link()
    {
        var request = Request() with { ETag = "  \"v1\"  ", LastModified = ObservedAt.AddTicks(1234567) };
        var result = Result(CollectionOutcome.NotModified, request) with
        {
            Response = Response(304, "\"v1\"", []),
            SentValidators = new HttpRequestValidators { ETag = "\"v1\"", LastModified = ObservedAt },
            RequestCount = 2
        };
        var store = new FakeStore { Prior = Captured("prior", request.SourceId, request.Url, request.Url) };

        var decision = await Handler(result, store).ExecuteAsync("normalized-304", request);

        Assert.Equal(new SentValidators("\"v1\"", ObservedAt), store.Existing!.SentValidators);
        Assert.Equal(PriorCaptureLinkStatus.Linked, decision.PriorCaptureLinkStatus);
    }

    [Fact]
    public async Task MapsFailureAndDeferredOutcomesIncludingRetryDelayAndResponseMetadata()
    {
        var request = Request();
        var failedResult = Result(CollectionOutcome.Failed, request) with
        {
            FailureCode = CollectionFailureCode.TransportError
        };
        var failedStore = new FakeStore();
        await Handler(failedResult, failedStore).ExecuteAsync("failed", request);
        var failed = Assert.IsType<FailedAttemptResult>(failedStore.Received!.AttemptResult);
        Assert.Equal("TransportError", failed.FailureCode);
        Assert.Null(failed.Response);

        var deferredResult = Result(CollectionOutcome.Deferred, request) with
        {
            Response = Response(429, null, []),
            FailureCode = CollectionFailureCode.RateLimited,
            RetryAfterSeconds = 23,
            RequestCount = 2
        };
        var deferredStore = new FakeStore();
        await Handler(deferredResult, deferredStore).ExecuteAsync("deferred", request);
        var deferred = Assert.IsType<DeferredAttemptResult>(deferredStore.Received!.AttemptResult);
        Assert.Equal("RateLimited", deferred.FailureCode);
        Assert.Equal(TimeSpan.FromSeconds(23), deferred.RetryDelay);
        Assert.Equal(429, deferred.Response!.StatusCode);
    }

    [Fact]
    public async Task RejectsInvalidReceiptBeforePersistence()
    {
        var request = Request();
        var invalid = Result(CollectionOutcome.Captured, request) with { Capture = null };
        var store = new FakeStore();

        await Assert.ThrowsAsync<InvalidDataException>(() => Handler(invalid, store)
            .ExecuteAsync("attempt", request));

        Assert.Null(store.Received);
    }

    [Fact]
    public async Task CollectorFailureAndCancellationNeverInvokePersistence()
    {
        var request = Request();
        var store = new FakeStore();
        var failedCollector = new FakeCollector((_, _) => throw new InvalidOperationException("verification failed"));
        await Assert.ThrowsAsync<InvalidOperationException>(() => new CollectAndImportCollectionAttempt(failedCollector, store)
            .ExecuteAsync("attempt", request));
        Assert.Null(store.Received);

        using var canceled = new CancellationTokenSource();
        canceled.Cancel();
        var canceledCollector = new FakeCollector((_, token) =>
        {
            token.ThrowIfCancellationRequested();
            return Task.FromResult(Result(CollectionOutcome.Failed, request));
        });
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CollectAndImportCollectionAttempt(canceledCollector, store)
            .ExecuteAsync("attempt", request, canceled.Token));
        Assert.Null(store.Received);
    }

    [Fact]
    public async Task CancellationAfterCollectorReturnsStillSkipsPersistence()
    {
        var request = Request();
        var result = Result(CollectionOutcome.Failed, request) with { FailureCode = CollectionFailureCode.TransportError };
        using var canceled = new CancellationTokenSource();
        var collector = new FakeCollector((_, _) =>
        {
            canceled.Cancel();
            return Task.FromResult(result);
        });
        var store = new FakeStore();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => new CollectAndImportCollectionAttempt(collector, store)
            .ExecuteAsync("attempt", request, canceled.Token));

        Assert.Null(store.Received);
    }

    [Fact]
    public async Task RetainedEvidenceExcludesDispositionAndStoreDistinguishesDuplicateFromConflict()
    {
        var request = Request();
        var result = Result(CollectionOutcome.Captured, request) with
        {
            Response = Response(200, "\"v1\"", ["br", "gzip"]),
            Capture = Capture(),
            BytesReceived = 12,
            RequestCount = 2
        };
        var store = new FakeStore();
        var handler = Handler(result, store);
        var first = await handler.ExecuteAsync("attempt", request);
        var duplicate = await handler.ExecuteAsync("attempt", request);

        Assert.Equal(ImportDisposition.NewAttempt, first.Disposition);
        Assert.Equal(ImportDisposition.DuplicateAttempt, duplicate.Disposition);
        Assert.Equal(first.AttemptResult, store.Existing!.AttemptResult);

        var conflictingResult = result with { Response = Response(200, "\"other\"", ["br", "gzip"]) };
        await Assert.ThrowsAsync<InvalidOperationException>(() => Handler(conflictingResult, store)
            .ExecuteAsync("attempt", request));
    }

    [Fact]
    public async Task DuplicateUnresolved304DoesNotGainLaterPriorCaptureLink()
    {
        var request = Request() with { ETag = "\"v1\"" };
        var result = Result(CollectionOutcome.NotModified, request) with
        {
            Response = Response(304, "\"v1\"", []),
            SentValidators = new HttpRequestValidators { ETag = request.ETag, LastModified = request.LastModified },
            RequestCount = 2
        };
        var store = new FakeStore();
        var handler = Handler(result, store);
        var first = await handler.ExecuteAsync("attempt-304", request);
        store.Prior = Captured("prior", request.SourceId, request.Url, request.Url);
        var duplicate = await handler.ExecuteAsync("attempt-304", request);

        Assert.Equal(PriorCaptureLinkStatus.Unresolved, first.PriorCaptureLinkStatus);
        Assert.Equal(ImportDisposition.DuplicateAttempt, duplicate.Disposition);
        Assert.Equal(PriorCaptureLinkStatus.Unresolved, duplicate.PriorCaptureLinkStatus);
        Assert.Null(store.Existing!.PriorCapturedAttempt);
    }

    [Fact]
    public async Task RejectsRetryDelayOutsideDomainRangeBeforePersistence()
    {
        var request = Request();
        var invalid = Result(CollectionOutcome.Deferred, request) with
        {
            Response = Response(429, null, []),
            FailureCode = CollectionFailureCode.RateLimited,
            RetryAfterSeconds = long.MaxValue,
            RequestCount = 2
        };
        var store = new FakeStore();

        await Assert.ThrowsAsync<InvalidDataException>(() => Handler(invalid, store)
            .ExecuteAsync("attempt", request));

        Assert.Null(store.Received);
    }

    [Fact]
    public async Task MapsLargestWholeSecondRetryDelaySupportedByDomain()
    {
        var request = Request();
        var seconds = TimeSpan.MaxValue.Ticks / TimeSpan.TicksPerSecond;
        var result = Result(CollectionOutcome.Deferred, request) with
        {
            Response = Response(429, null, []),
            FailureCode = CollectionFailureCode.RateLimited,
            RetryAfterSeconds = seconds,
            RequestCount = 2
        };
        var store = new FakeStore();

        await Handler(result, store).ExecuteAsync("attempt", request);

        var deferred = Assert.IsType<DeferredAttemptResult>(store.Received!.AttemptResult);
        Assert.Equal(TimeSpan.FromTicks(seconds * TimeSpan.TicksPerSecond), deferred.RetryDelay);
    }

    [Fact]
    public void StoredAttemptRejectsAClaimedPriorLinkThatValidatorsCannotProve()
    {
        var request = Request() with { ETag = "\"different\"" };
        var prior = Captured("prior", request.SourceId, request.Url, request.Url);
        var unchanged = new NotModifiedAttemptResult("attempt", request.SourceId, request.Url, request.Url,
            ObservedAt.AddMinutes(1), new CollectionResponse(304, "\"v1\"", null, null, []));

        Assert.Throws<ArgumentException>(() => new StoredCollectionAttempt(unchanged,
            new SentValidators(request.ETag, null), prior));
    }

    private static CollectAndImportCollectionAttempt Handler(CollectionResult result, FakeStore store) =>
        new(new FakeCollector((_, _) => Task.FromResult(result)), store);

    private static CollectionRequest Request() => new()
    {
        JobId = "job-1",
        SourceId = "source-1",
        Url = "https://example.test/page",
        AllowedOrigin = "https://example.test",
        AllowedPathPrefix = "/page",
        ArtifactDirectory = Path.GetFullPath("artifacts")
    };

    private static CollectionResult Result(CollectionOutcome outcome, CollectionRequest request) => new()
    {
        JobId = request.JobId,
        SourceId = request.SourceId,
        RequestedUrl = request.Url,
        FinalUrl = request.Url,
        Outcome = outcome,
        ObservedAt = ObservedAt,
        BytesReceived = 0,
        RequestCount = 1
    };

    private static HttpResponseMetadata Response(int status, string? etag, string[] encodings) => new()
    {
        StatusCode = status,
        ETag = etag,
        LastModified = ObservedAt,
        ContentType = status == 304 ? null : "text/html; charset=utf-8",
        ContentEncodings = encodings
    };

    private static CaptureArtifact Capture() => new()
    {
        Sha256 = new string('a', 64),
        RelativePath = new string('a', 64) + ".gz",
        ByteLength = 12
    };

    private static CapturedAttemptResult Captured(string id, string source, string url, string final) =>
        new(id, source, url, final, ObservedAt,
            new CollectionResponse(200, "\"v1\"", ObservedAt, "text/html", ["br", "gzip"]),
            new CaptureIdentity(new string('a', 64), 12));

    private sealed class FakeCollector(Func<CollectionRequest, CancellationToken, Task<CollectionResult>> execute)
        : ICollectorProcess
    {
        public Task<CollectionResult> RunAsync(CollectionRequest request, CancellationToken cancellationToken) =>
            execute(request, cancellationToken);
    }

    private sealed class FakeStore : ICollectionAttemptStore
    {
        public CollectionAttemptImport? Received { get; private set; }
        public CapturedAttemptResult? Prior { get; set; }
        public StoredCollectionAttempt? Existing { get; private set; }

        public Task<CollectionImportDecision> ImportAtomicallyAsync(CollectionAttemptImport attempt,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            Received = attempt;
            var decision = attempt.Decide(Existing, Prior);
            if (decision.Disposition == ImportDisposition.NewAttempt)
                Existing = new StoredCollectionAttempt(decision.AttemptResult, decision.SentValidators,
                    decision.PriorCapturedAttempt);
            return Task.FromResult(decision);
        }
    }

}
