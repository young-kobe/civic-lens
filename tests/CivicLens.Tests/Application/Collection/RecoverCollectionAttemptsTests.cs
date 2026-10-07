using CivicLens.Application.Collection;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;

namespace CivicLens.Tests.Application.Collection;

public sealed class RecoverCollectionAttemptsTests
{
    private static readonly DateTimeOffset ObservedAt = new(2026, 10, 6, 12, 0, 0, TimeSpan.Zero);

    [Fact]
    public async Task CancellationDuringSecondImportReturnsPriorConfirmationAndMarksPendingEntry()
    {
        using var cancellation = new CancellationTokenSource();
        var handoffs = new FakeHandoffs(Handoff("entry-1"), Handoff("entry-2"));
        var store = new FakeAttemptStore(cancellation, cancelOnImportNumber: 2);
        var recovery = new RecoverCollectionAttempts(handoffs, new FakeVerifier(), store);

        var results = await recovery.ReplayAllAsync(Path.GetTempPath(), cancellation.Token);

        Assert.Collection(results,
            first =>
            {
                Assert.Equal("entry-1", first.HandoffId);
                Assert.Equal(CollectionRecoveryStatus.ImportedAndRemoved, first.Status);
                Assert.NotNull(first.Decision);
            },
            second =>
            {
                Assert.Equal("entry-2", second.HandoffId);
                Assert.Equal(CollectionRecoveryStatus.Cancelled, second.Status);
                Assert.Null(second.Decision);
            });
        Assert.Equal(new[] { "entry-2" }, await handoffs.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CancellationAfterCommitBeforeCleanupPreservesConfirmationAndStopsBatch()
    {
        using var cancellation = new CancellationTokenSource();
        var handoffs = new FakeHandoffs(Handoff("entry-1"), Handoff("entry-2"));
        var store = new FakeAttemptStore(cancellation, cancelAfterCommitOnImportNumber: 1);
        var recovery = new RecoverCollectionAttempts(handoffs, new FakeVerifier(), store);

        var results = await recovery.ReplayAllAsync(Path.GetTempPath(), cancellation.Token);

        var completed = Assert.Single(results);
        Assert.Equal("entry-1", completed.HandoffId);
        Assert.Equal(CollectionRecoveryStatus.ImportedCleanupFailed, completed.Status);
        Assert.NotNull(completed.Decision);
        Assert.False(string.IsNullOrWhiteSpace(completed.Error));
        Assert.Equal(new[] { "entry-1", "entry-2" }, await handoffs.ListAsync(CancellationToken.None));
    }

    [Fact]
    public async Task CancellationAfterSuccessfulCleanupReportsRemainingWorkAsCancelled()
    {
        using var cancellation = new CancellationTokenSource();
        var handoffs = new FakeHandoffs(Captured("entry-1", 'a'), Captured("entry-2", 'b'))
        {
            AfterDelete = cancellation.Cancel
        };
        var recovery = new RecoverCollectionAttempts(handoffs, new FakeVerifier(), new FakeAttemptStore(cancellation));

        var results = await recovery.ReplayAllAsync(Path.GetTempPath(), cancellation.Token);

        Assert.Equal(new[] { CollectionRecoveryStatus.ImportedAndRemoved, CollectionRecoveryStatus.Cancelled },
            results.Select(result => result.Status));
        Assert.Equal(new[] { "entry-2" }, await handoffs.ListAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public async Task BatchImportsCapturedEvidenceBefore304RegardlessOfAttemptIdOrder(bool ambiguous)
    {
        using var cancellation = new CancellationTokenSource();
        var handoffs = new FakeHandoffs(NotModified("a-304"), Captured("b-capture", 'a'),
            Captured("z-capture", ambiguous ? 'b' : 'a'));
        var store = new FakeAttemptStore(cancellation);

        var results = await new RecoverCollectionAttempts(handoffs, new FakeVerifier(), store)
            .ReplayAllAsync(Path.GetTempPath());

        Assert.Equal(new[] { "b-capture", "z-capture", "a-304" }, results.Select(result => result.HandoffId));
        var unchanged = results.Last().Decision!;
        Assert.Equal(ambiguous ? PriorCaptureLinkStatus.Unresolved : PriorCaptureLinkStatus.Linked,
            unchanged.PriorCaptureLinkStatus);
        Assert.Empty(await handoffs.ListAsync(CancellationToken.None));
    }

    [Theory]
    [InlineData("import")]
    [InlineData("read")]
    [InlineData("cleanup")]
    public async Task BatchDefers304UntilCapturedPrerequisitesAreConfirmed(string failureStage)
    {
        using var cancellation = new CancellationTokenSource();
        var handoffs = new FakeHandoffs(NotModified("a-304"), Captured("b-capture", 'a'), Captured("z-capture", 'b'))
        {
            InvalidId = failureStage == "read" ? "z-capture" : null,
            CleanupFailureId = failureStage == "cleanup" ? "b-capture" : null
        };
        var store = new FakeAttemptStore(cancellation) { FailureId = failureStage == "import" ? "z-capture" : null };
        var recovery = new RecoverCollectionAttempts(handoffs, new FakeVerifier(), store);

        var results = await recovery.ReplayAllAsync(Path.GetTempPath());

        var unchanged = Assert.Single(results, result => result.HandoffId == "a-304");
        if (failureStage == "cleanup")
        {
            Assert.Equal(CollectionRecoveryStatus.ImportedAndRemoved, unchanged.Status);
            Assert.Equal(PriorCaptureLinkStatus.Unresolved, unchanged.Decision!.PriorCaptureLinkStatus);
            Assert.Equal(new[] { "b-capture" }, await handoffs.ListAsync(CancellationToken.None));
            return;
        }
        Assert.Equal(CollectionRecoveryStatus.PrerequisiteUnconfirmed, unchanged.Status);
        Assert.Null(unchanged.Decision);
        Assert.Equal(new[] { "a-304", "z-capture" }, await handoffs.ListAsync(CancellationToken.None));
        handoffs.InvalidId = null;
        store.FailureId = null;
        var recovered = await recovery.ReplayAllAsync(Path.GetTempPath());
        Assert.Equal(PriorCaptureLinkStatus.Unresolved, recovered.Last().Decision!.PriorCaptureLinkStatus);
    }

    private static PendingCollectionHandoff Captured(string id, char hashDigit)
    {
        var handoff = Handoff(id);
        return handoff with
        {
            Receipt = handoff.Receipt with
            {
                Outcome = CollectionOutcome.Captured,
                FailureCode = null,
                Response = new HttpResponseMetadata { StatusCode = 200, ETag = "\"v1\"", ContentEncodings = [] },
                Capture = new CaptureArtifact { Sha256 = new string(hashDigit, 64), RelativePath = new string(hashDigit, 64) + ".gz", ByteLength = 4 },
                RequestCount = 2,
                BytesReceived = 4
            }
        };
    }

    private static PendingCollectionHandoff NotModified(string id)
    {
        var handoff = Handoff(id);
        return handoff with
        {
            Request = handoff.Request with { ETag = "\"v1\"" },
            Receipt = handoff.Receipt with
            {
                Outcome = CollectionOutcome.NotModified,
                FailureCode = null,
                Response = new HttpResponseMetadata { StatusCode = 304, ETag = "\"v1\"", ContentEncodings = [] },
                SentValidators = new HttpRequestValidators { ETag = "\"v1\"" },
                ObservedAt = ObservedAt.AddMinutes(1),
                RequestCount = 2
            }
        };
    }

    private static PendingCollectionHandoff Handoff(string id)
    {
        var request = new CollectionRequest
        {
            JobId = "job-" + id,
            SourceId = "source",
            Url = "https://example.test/page",
            AllowedOrigin = "https://example.test",
            AllowedPathPrefix = "/page",
            ArtifactDirectory = Path.GetFullPath("captures")
        };
        var receipt = new CollectionResult
        {
            JobId = request.JobId,
            SourceId = request.SourceId,
            RequestedUrl = request.Url,
            FinalUrl = request.Url,
            Outcome = CollectionOutcome.Failed,
            FailureCode = CollectionFailureCode.TransportError,
            ObservedAt = ObservedAt,
            BytesReceived = 0,
            RobotsRequestCount = request.Version >= 6 ? 1 : null,
            RequestCount = 1
        };
        return new PendingCollectionHandoff(PendingCollectionHandoff.CurrentVersion, id, request, receipt);
    }

    private sealed class FakeHandoffs(params PendingCollectionHandoff[] handoffs) : ICollectionReceiptHandoffStore
    {
        private readonly Dictionary<string, PendingCollectionHandoff> entries = handoffs.ToDictionary(item => item.AttemptId);
        public string? InvalidId { get; set; }
        public string? CleanupFailureId { get; init; }
        public Action? AfterDelete { get; init; }

        public ValueTask<IAsyncDisposable> AcquireRecoveryLeaseAsync(CancellationToken cancellationToken) =>
            ValueTask.FromResult<IAsyncDisposable>(new NoopLease());

        public Task SaveAsync(PendingCollectionHandoff handoff, CancellationToken cancellationToken)
        {
            entries.Add(handoff.AttemptId, handoff);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<string>> ListAsync(CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<string>>(entries.Keys.Order(StringComparer.Ordinal).ToArray());

        public Task<PendingCollectionHandoff> LoadAsync(string handoffId, CancellationToken cancellationToken) =>
            handoffId == InvalidId ? throw new InvalidDataException("Unreadable envelope.") : Task.FromResult(entries[handoffId]);

        public Task DeleteAsync(string handoffId, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            if (handoffId == CleanupFailureId) throw new IOException("Cleanup failed.");
            entries.Remove(handoffId);
            AfterDelete?.Invoke();
            return Task.CompletedTask;
        }

        private sealed class NoopLease : IAsyncDisposable
        {
            public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        }
    }

    private sealed class FakeVerifier : ICaptureArtifactVerifier
    {
        public Task VerifyAsync(CollectionRequest request, CollectionResult receipt, string artifactRoot,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            receipt.ValidateAgainst(request);
            return Task.CompletedTask;
        }
    }

    private sealed class FakeAttemptStore(CancellationTokenSource cancellation,
        int cancelOnImportNumber = -1, int cancelAfterCommitOnImportNumber = -1) : ICollectionAttemptStore
    {
        private int importCount;
        private readonly Dictionary<string, StoredCollectionAttempt> retained = [];
        public Task<StoredCollectionAttempt?> GetAsync(string attemptId, CancellationToken cancellationToken) =>
            Task.FromResult(retained.GetValueOrDefault(attemptId));
        public string? FailureId { get; set; }

        public Task<CollectionImportDecision> ImportAtomicallyAsync(CollectionAttemptImport attempt,
            CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            importCount++;
            if (importCount == cancelOnImportNumber)
            {
                cancellation.Cancel();
                throw new OperationCanceledException(cancellation.Token);
            }

            if (attempt.AttemptResult.AttemptId == FailureId) throw new IOException("Import failed.");
            retained.TryGetValue(attempt.AttemptResult.AttemptId, out var existing);
            var decision = attempt.DecideFromCandidates(existing,
                retained.Values.Select(item => item.AttemptResult).OfType<CapturedAttemptResult>());
            retained[attempt.AttemptResult.AttemptId] = new StoredCollectionAttempt(decision.AttemptResult,
                decision.SentValidators, decision.PriorCapturedAttempt);
            if (importCount == cancelAfterCommitOnImportNumber)
                cancellation.Cancel();
            return Task.FromResult(decision);
        }
    }
}
