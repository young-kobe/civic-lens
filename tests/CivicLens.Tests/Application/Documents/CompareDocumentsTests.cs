using CivicLens.Application.Documents;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;

namespace CivicLens.Tests.Application.Documents;

public sealed class CompareDocumentsTests
{
    [Fact]
    public async Task PassesBothExtractionIdsToTheStoreAndReturnsItsComparison()
    {
        var before = Extraction("old");
        var after = Extraction("new");
        var comparisons = new ComparisonStore(DocumentComparison.Create(before, after));

        var actual = await new CompareDocuments(comparisons).ExecuteAsync(before.ExtractionId, after.ExtractionId);

        Assert.Same(comparisons.Result, actual);
        Assert.Equal((before.ExtractionId, after.ExtractionId), comparisons.Requested);
        await Assert.ThrowsAsync<ArgumentException>(() => new CompareDocuments(comparisons).ExecuteAsync(" ", after.ExtractionId));
    }

    private static DocumentExtraction Extraction(string text)
    {
        var id = text == "old" ? "before" : "after";
        var attempt = new CapturedAttemptResult(id, "source", "https://example.test/", "https://example.test/",
            DateTimeOffset.UnixEpoch, new CollectionResponse(200, null, null, "text/plain", []),
            new CaptureIdentity(new string('a', 64), 0));
        return new(attempt, "parser", "normalizer", text);
    }

    private sealed class ComparisonStore(DocumentComparison result) : IDocumentComparisonStore
    {
        public DocumentComparison Result { get; } = result;
        public (string Before, string After)? Requested { get; private set; }
        public Task<DocumentComparison?> GetAsync(string comparisonId, CancellationToken cancellationToken) =>
            Task.FromResult<DocumentComparison?>(Result.ComparisonId == comparisonId ? Result : null);
        public Task<DocumentComparison> CompareAndSaveAsync(string beforeExtractionId, string afterExtractionId,
            CancellationToken cancellationToken)
        { Requested = (beforeExtractionId, afterExtractionId); return Task.FromResult(Result); }
    }
}
