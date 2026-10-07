using CivicLens.Core.Collection;
using CivicLens.Core.Documents;

namespace CivicLens.Tests.Core.Documents;

public sealed class DocumentComparisonTests
{
    [Fact]
    public void CreatesDeterministicHunksWithExactUtf16RangesAndRepeatedLineContext()
    {
        var before = Extraction("one\nrepeat\nA😀 old\nrepeat\nlast\n");
        var after = Extraction("one\nrepeat\nA😀 new\nrepeat\nlast\n");

        var result = DocumentComparison.Create(before, after);

        Assert.Equal(DocumentComparisonStatus.Complete, result.Status);
        var hunk = Assert.Single(result.Hunks);
        Assert.Equal("A😀 old\n", before.Text.Substring(hunk.BeforeStart, hunk.BeforeLength));
        Assert.Equal("A😀 new\n", after.Text.Substring(hunk.AfterStart, hunk.AfterLength));
        Assert.Equal("repeat\nA😀 old\nrepeat\n", hunk.BeforeContext);
        Assert.Equal(before.Text.IndexOf("repeat\nA😀 old", StringComparison.Ordinal), hunk.BeforeContextStart);
        Assert.Equal(result.ComparisonId, DocumentComparison.Create(before, after).ComparisonId);
        Assert.Contains("old", before.Text.Substring(hunk.WordEdits[0].BeforeStart, hunk.WordEdits[0].BeforeLength));
        Assert.Contains("new", after.Text.Substring(hunk.WordEdits[0].AfterStart, hunk.WordEdits[0].AfterLength));
        Assert.Equal(before.ExtractionId, result.BeforeExtractionId);
        Assert.Equal(after.ExtractionId, result.AfterExtractionId);
        Assert.Single(hunk.WordEdits);
    }

    [Fact]
    public void PreservesInsertionPointAndReportsIncompatibleProvenanceWithoutHunks()
    {
        var before = Extraction("alpha\nomega");
        var after = Extraction("alpha\ninserted\nomega");
        var insertion = Assert.Single(DocumentComparison.Create(before, after).Hunks);
        Assert.Equal(0, insertion.BeforeLength);
        Assert.Equal(before.Text.IndexOf("omega", StringComparison.Ordinal), insertion.BeforeStart);
        Assert.Equal("inserted\n", after.Text.Substring(insertion.AfterStart, insertion.AfterLength));

        var otherUrl = Extraction("alpha\nomega", url: "https://example.test/other");
        var incompatible = DocumentComparison.Create(before, otherUrl);
        Assert.Equal(DocumentComparisonStatus.Incompatible, incompatible.Status);
        Assert.NotNull(incompatible.Reason);
        Assert.Empty(incompatible.Hunks);
    }

    [Fact]
    public void ReturnsLimitResultWithoutPartialDiffAndHonorsCancellation()
    {
        var before = Extraction(string.Join('\n', Enumerable.Range(0, 1_000)));
        var after = Extraction(string.Join('\n', Enumerable.Range(1_000, 1_000)));
        var limited = DocumentComparison.Create(before, after);
        Assert.Equal(DocumentComparisonStatus.LimitExceeded, limited.Status);
        Assert.Empty(limited.Hunks);

        using var source = new CancellationTokenSource();
        source.Cancel();
        Assert.Throws<OperationCanceledException>(() => DocumentComparison.Create(before, after, source.Token));
    }

    [Fact]
    public void RequiresEqualProfileRevisionAndProcessingVersions()
    {
        var source = Attempt("attempt");
        var first = new DocumentExtraction(source, "parser", "normalizer", "old", new("p", "main", []));
        var changedProfile = new DocumentExtraction(source, "parser", "normalizer", "new", new("p", "article", []));
        Assert.Equal(DocumentComparisonStatus.Incompatible, DocumentComparison.Create(first, changedProfile).Status);
        Assert.Equal(DocumentComparisonStatus.Incompatible,
            DocumentComparison.Create(first, new(source, "parser-2", "normalizer", "new", new("p", "main", []))).Status);
    }

    [Fact]
    public void WordEditsRetainWhitespaceAndSeparateChangesInsideOneLine()
    {
        var before = Extraction("alpha old middle old omega");
        var after = Extraction("alpha new middle new omega");
        var hunk = Assert.Single(DocumentComparison.Create(before, after).Hunks);
        Assert.Equal(2, hunk.WordEdits.Length);
        Assert.Equal("old", before.Text.Substring(hunk.WordEdits[0].BeforeStart, hunk.WordEdits[0].BeforeLength));
        Assert.Equal("new", after.Text.Substring(hunk.WordEdits[0].AfterStart, hunk.WordEdits[0].AfterLength));

        var spacingBefore = Extraction("a b");
        var spacingAfter = Extraction("a  b");
        var spacingHunk = Assert.Single(DocumentComparison.Create(spacingBefore, spacingAfter).Hunks);
        var whitespaceEdit = Assert.Single(spacingHunk.WordEdits);
        Assert.Equal(" ", spacingBefore.Text.Substring(whitespaceEdit.BeforeStart, whitespaceEdit.BeforeLength));
        Assert.Equal("  ", spacingAfter.Text.Substring(whitespaceEdit.AfterStart, whitespaceEdit.AfterLength));
    }

    [Fact]
    public void OversizedUnicodeContextReturnsLimitWithoutClippedEvidence()
    {
        var context = string.Concat(Enumerable.Repeat("😀", 5000));
        var result = DocumentComparison.Create(Extraction(context + "\nold"), Extraction(context + "\nnew"));
        Assert.Equal(DocumentComparisonStatus.LimitExceeded, result.Status);
        Assert.Empty(result.Hunks);
    }

    private static DocumentExtraction Extraction(string text, string url = "https://example.test/") =>
        new(Attempt(Guid.NewGuid().ToString("N"), url), "parser", "normalizer", text);

    private static CapturedAttemptResult Attempt(string id, string url = "https://example.test/") =>
        new(id, "source", url, url, DateTimeOffset.UnixEpoch, new CollectionResponse(200, null, null, "text/html", []),
            new CaptureIdentity(new string('a', 64), 0));
}
