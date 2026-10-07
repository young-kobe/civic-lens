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

    [Fact]
    public async Task ConfiguredExtractionUsesAssignedProfileForHistoricalDisabledSource()
    {
        var attempt = Captured("attempt");
        var attempts = new AttemptStore(new StoredCollectionAttempt(attempt, null, null));
        var extractor = new TextExtractor("article text");
        var handler = new ExtractDocument(attempts, extractor, new ExtractionStore());
        var configuration = Configuration() with
        {
            Sources = [Configuration().Sources[0] with { Enabled = false }]
        };
        var profile = new DocumentContentProfile("article", "main", [".nav"]);

        var extraction = await handler.ExecuteConfiguredAsync(configuration, "attempt", "/artifacts");

        Assert.True(profile.Matches(extractor.LastProfile));
        Assert.True(profile.Matches(extraction.Profile));
    }

    [Fact]
    public async Task ConfiguredExtractionRejectsMissingProfileAssignment()
    {
        var handler = new ExtractDocument(new AttemptStore(new StoredCollectionAttempt(Captured("attempt"), null, null)),
            new TextExtractor("text"), new ExtractionStore());
        var configuration = Configuration() with
        {
            Sources = [Configuration().Sources[0] with { DocumentProfileId = null }]
        };

        await Assert.ThrowsAsync<ArgumentException>(() => handler.ExecuteConfiguredAsync(configuration, "attempt", "/artifacts"));
    }

    [Fact]
    public async Task ConfigurationIsSnapshottedBeforeAwaitingStoredEvidence()
    {
        var configuration = Configuration();
        var attempt = Captured("attempt");
        var attempts = new AttemptStore(new StoredCollectionAttempt(attempt, null, null), () =>
        {
            configuration.DocumentProfiles![0] = configuration.DocumentProfiles[0] with { Selector = "#changed" };
        });
        var handler = new ExtractDocument(attempts, new TextExtractor("text"), new ExtractionStore());
        var extraction = await handler.ExecuteConfiguredAsync(configuration, "attempt", "/artifacts");
        Assert.Equal("main", extraction.Profile!.Selector);
    }

    private static CollectionConfiguration Configuration() => new()
    {
        Version = 2,
        People = [new PersonConfiguration { Id = "person", Name = "Person" }],
        DocumentProfiles = [new DocumentProfileConfiguration { Id = "article", Selector = "main", ExcludedSelectors = [".nav"] }],
        Sources =
        [
            new WatchedSourceConfiguration
            {
                Id = "source", DocumentProfileId = "article",
                Coverage = [new SourceCoverageConfiguration { PersonId = "person", StartsOn = new DateOnly(2020, 1, 1) }],
                Url = "https://example.test/", AllowedOrigin = "https://example.test", AllowedPathPrefix = "/"
            }
        ]
    };

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
        public DocumentContentProfile? LastProfile { get; private set; }

        public Task<string> ExtractAsync(CapturedAttemptResult attempt, string artifactRoot,
            DocumentContentProfile? profile, CancellationToken cancellationToken)
        {
            Calls++;
            LastAttempt = attempt;
            LastProfile = profile;
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

    private sealed class AttemptStore(StoredCollectionAttempt? result, Action? onRead = null) : ICollectionAttemptStore
    {
        public Task<StoredCollectionAttempt?> GetAsync(string attemptId, CancellationToken cancellationToken)
        {
            onRead?.Invoke();
            return Task.FromResult(result?.AttemptResult.AttemptId == attemptId ? result : null);
        }

        public Task<CivicLens.Core.Collection.CollectionImportDecision> ImportAtomicallyAsync(
            CollectionAttemptImport attempt, CancellationToken cancellationToken) =>
            throw new NotSupportedException();
    }
}
