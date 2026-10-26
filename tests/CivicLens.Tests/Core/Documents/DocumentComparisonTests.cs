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
        var first = new DocumentExtraction(source, "parser", "old", new("p", "main", []));
        var changedProfile = new DocumentExtraction(source, "parser", "new", new("p", "article", []));
        Assert.Equal(DocumentComparisonStatus.Incompatible, DocumentComparison.Create(first, changedProfile).Status);
        Assert.Equal(DocumentComparisonStatus.Incompatible,
            DocumentComparison.Create(first, new(source, "extraction-2", "new", new("p", "main", []))).Status);
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

    private const string BumpMessage = "Comparison output or limits changed. Bump DocumentComparison.CurrentAlgorithmVersion " +
        "or CurrentSettingsVersion, update the version table in docs/architecture.md, and keep the old implementation " +
        "for historical reads. Then update this test. Actual: ";

    [Fact]
    public void ComparisonOutputAndSettingsMatchTheirPinnedVersions()
    {
        var result = DocumentComparison.Create(Extraction("one\nold word\nlast"), Extraction("one\nnew word\nlast"));
        var actual = System.Text.Json.JsonSerializer.Serialize(result.Hunks);
        var settings = new[]
        {
            DocumentComparison.MaximumLines, DocumentComparison.MaximumMatrixCells, DocumentComparison.MaximumChangedTextLength,
            DocumentComparison.ContextLines, DocumentComparison.MaximumContextTextLength, DocumentComparison.MaximumWordTokens,
            DocumentComparison.MaximumWordMatrixCells, DocumentComparison.MaximumTotalWordMatrixCells
        };

        Assert.Equal("line-lcs-context-v1", DocumentComparison.CurrentAlgorithmVersion);
        Assert.Equal("bounded-lines-2000-cells-1000000-change-8192-context-1-word-tokens-512-cells-250000-total-1000000-v1",
            DocumentComparison.CurrentSettingsVersion);
        Assert.True(settings.SequenceEqual([2_000, 1_000_000, 8_192, 1, 8_192, 512, 250_000, 1_000_000]), BumpMessage + string.Join(",", settings));
        Assert.True(actual == """[{"BeforeStart":4,"BeforeLength":9,"AfterStart":4,"AfterLength":9,"BeforeText":"old word\n","AfterText":"new word\n","BeforeContextStart":0,"BeforeContext":"one\nold word\nlast","AfterContextStart":0,"AfterContext":"one\nnew word\nlast","WordEdits":[{"BeforeStart":4,"BeforeLength":3,"AfterStart":4,"AfterLength":3}]}]""", BumpMessage + actual);
    }

    private static DocumentExtraction Extraction(string text, string url = "https://example.test/") =>
        new(Attempt(Guid.NewGuid().ToString("N"), url), "extraction", text);

    private static CapturedAttemptResult Attempt(string id, string url = "https://example.test/") =>
        new(id, "source", url, url, DateTimeOffset.UnixEpoch, new CollectionResponse(200, null, null, "text/html", []),
            new CaptureIdentity(new string('a', 64), 0));
}
