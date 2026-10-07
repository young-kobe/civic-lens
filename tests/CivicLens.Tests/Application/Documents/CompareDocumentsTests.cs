using CivicLens.Application.Documents;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;

namespace CivicLens.Tests.Application.Documents;

public sealed class CompareDocumentsTests
{
    [Fact]
    public async Task LoadsExactExtractionsCreatesAndRetainsComparison()
    {
        var before = Extraction("old");
        var after = Extraction("new");
        var extractions = new ExtractionStore(before, after);
        var comparisons = new ComparisonStore();

        var actual = await new CompareDocuments(extractions, comparisons)
            .ExecuteAsync(before.ExtractionId, after.ExtractionId);

        Assert.Same(actual, comparisons.Saved);
        Assert.Equal(before.ExtractionId, actual.BeforeExtractionId);
        Assert.Equal(after.ExtractionId, actual.AfterExtractionId);
        Assert.Equal(DocumentComparisonStatus.Complete, actual.Status);
        Assert.Same(actual, await new GetDocumentComparison(comparisons).ExecuteAsync(actual.ComparisonId));
        await Assert.ThrowsAsync<ArgumentException>(() => new CompareDocuments(extractions, comparisons)
            .ExecuteAsync("missing", after.ExtractionId));
    }

    private static DocumentExtraction Extraction(string text)
    {
        var id = text == "old" ? "before" : "after";
        var attempt = new CapturedAttemptResult(id, "source", "https://example.test/", "https://example.test/",
            DateTimeOffset.UnixEpoch, new CollectionResponse(200, null, null, "text/plain", []),
            new CaptureIdentity(new string('a', 64), 0));
        return new(attempt, "parser", "normalizer", text);
    }

    private sealed class ExtractionStore(params DocumentExtraction[] values) : IDocumentExtractionStore
    {
        public Task<DocumentExtraction?> GetAsync(string extractionId, CancellationToken cancellationToken) =>
            Task.FromResult(values.SingleOrDefault(value => value.ExtractionId == extractionId));
        public Task<DocumentExtraction> SaveAsync(DocumentExtraction extraction, CancellationToken cancellationToken) =>
            Task.FromResult(extraction);
    }

    private sealed class ComparisonStore : IDocumentComparisonStore
    {
        public DocumentComparison? Saved { get; private set; }
        public Task<DocumentComparison?> GetAsync(string comparisonId, CancellationToken cancellationToken) =>
            Task.FromResult(Saved?.ComparisonId == comparisonId ? Saved : null);
        public Task<DocumentComparison> SaveAsync(DocumentComparison comparison, CancellationToken cancellationToken)
        { Saved = comparison; return Task.FromResult(comparison); }
    }
}
