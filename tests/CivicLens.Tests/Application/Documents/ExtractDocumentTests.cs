using CivicLens.Application.Collection;
using CivicLens.Application.Documents;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;

namespace CivicLens.Tests.Application.Documents;

public sealed class ExtractDocumentTests
{
    [Fact]
    public async Task ReplaysTheImportedCaptureAndReturnsRetainedExtraction()
    {
        var attempt = Captured("attempt");
        var attempts = new AttemptStore(new StoredCollectionAttempt(attempt, null, null));
        var extractor = new TextExtractor("source text");
        var extractions = new ExtractionStore();
        var handler = new ExtractDocument(attempts, extractor, extractions);

        var first = await handler.ExecuteAsync("attempt", "/artifacts");
        var replay = await handler.ExecuteAsync("attempt", "/artifacts");

        Assert.Same(first, replay);
        Assert.Equal(2, extractor.Calls);
        Assert.Same(attempt, extractor.LastAttempt);
        Assert.Equal("source text", first.Text);
        Assert.Equal(2, extractions.Saves);
    }

    [Fact]
    public async Task MissingOrNoncapturedAttemptNeverRunsExtractorOrSaves()
    {
        var extractor = new TextExtractor("text");
        var store = new ExtractionStore();
        var missing = new ExtractDocument(new AttemptStore(null), extractor, store);
        await Assert.ThrowsAsync<ArgumentException>(() => missing.ExecuteAsync("missing", "/artifacts"));

        var failedAttempt = new FailedAttemptResult("failed", "source", "https://example.test/",
            "https://example.test/", DateTimeOffset.UnixEpoch, "network");
        var noncaptured = new ExtractDocument(new AttemptStore(new StoredCollectionAttempt(failedAttempt, null, null)),
            extractor, store);
        await Assert.ThrowsAsync<ArgumentException>(() => noncaptured.ExecuteAsync("failed", "/artifacts"));
        Assert.Equal(0, extractor.Calls);
        Assert.Equal(0, store.Saves);
    }

    [Fact]
    public async Task ExtractionFailureAndCancellationDoNotSave()
    {
        var attempt = Captured("attempt");
        var store = new ExtractionStore();
        var failure = new ExtractDocument(new AttemptStore(new StoredCollectionAttempt(attempt, null, null)),
            new TextExtractor(null, new InvalidOperationException("decode")), store);
        await Assert.ThrowsAsync<InvalidOperationException>(() => failure.ExecuteAsync("attempt", "/artifacts"));

        using var cancellation = new CancellationTokenSource();
        var cancelledExtractor = new TextExtractor("text", cancel: cancellation);
        var cancelled = new ExtractDocument(new AttemptStore(new StoredCollectionAttempt(attempt, null, null)),
            cancelledExtractor, store);
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() =>
            cancelled.ExecuteAsync("attempt", "/artifacts", cancellation.Token));
        Assert.Equal(0, store.Saves);
    }

    private static CapturedAttemptResult Captured(string id) => new(id, "source", "https://example.test/",
        "https://example.test/", DateTimeOffset.UnixEpoch,
        new CollectionResponse(200, null, null, "text/html", []), new CaptureIdentity(new string('a', 64), 4));

    private sealed class TextExtractor(string? text, Exception? failure = null, CancellationTokenSource? cancel = null)
        : IDocumentTextExtractor
    {
        public string ParserVersion => "parser-v1";
        public string NormalizationVersion => "normalization-v1";
        public int Calls { get; private set; }
        public CapturedAttemptResult? LastAttempt { get; private set; }

        public Task<string> ExtractAsync(CapturedAttemptResult attempt, string artifactRoot,
            CancellationToken cancellationToken)
        {
            Calls++;
            LastAttempt = attempt;
            if (failure is not null) throw failure;
            if (cancel is not null)
            {
                cancel.Cancel();
                cancellationToken.ThrowIfCancellationRequested();
            }
            return Task.FromResult(text!);
        }
    }

    private sealed class ExtractionStore : IDocumentExtractionStore
    {
        private readonly Dictionary<string, DocumentExtraction> records = [];
        public int Saves { get; private set; }

        public Task<DocumentExtraction?> GetAsync(string extractionId, CancellationToken cancellationToken) =>
            Task.FromResult(records.GetValueOrDefault(extractionId));

        public Task<DocumentExtraction> SaveAsync(DocumentExtraction extraction, CancellationToken cancellationToken)
        {
            Saves++;
            if (records.TryGetValue(extraction.ExtractionId, out var existing)) return Task.FromResult(existing);
            records.Add(extraction.ExtractionId, extraction);
            return Task.FromResult(extraction);
        }
    }

    private sealed class AttemptStore(StoredCollectionAttempt? result) : ICollectionAttemptStore
    {
        public Task<StoredCollectionAttempt?> GetAsync(string attemptId, CancellationToken cancellationToken) =>
            Task.FromResult(result?.AttemptResult.AttemptId == attemptId ? result : null);

        public Task<CivicLens.Core.Collection.CollectionImportDecision> ImportAtomicallyAsync(
            CollectionAttemptImport attempt, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
