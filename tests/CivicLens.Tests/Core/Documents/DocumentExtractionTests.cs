using CivicLens.Core.Collection;
using CivicLens.Core.Documents;

namespace CivicLens.Tests.Core.Documents;

public sealed class DocumentExtractionTests
{
    [Fact]
    public void IdentityIsFramedAndIndependentOfTextWhileTextHashTracksContent()
    {
        var attempt = Captured("ab");
        var first = new DocumentExtraction(attempt, "c", "d", "text");
        var changedText = new DocumentExtraction(attempt, "c", "d", "other");
        var ambiguousWithoutFraming = new DocumentExtraction(Captured("a"), "bc", "d", "text");

        Assert.Equal(first.ExtractionId, changedText.ExtractionId);
        Assert.Equal("febb2fc5e2b7f08feafc5d7b28689b27b3e76f20bc5503e74190d0964365d93d", first.ExtractionId);
        Assert.NotEqual(first.TextSha256, changedText.TextSha256);
        Assert.NotEqual(first.ExtractionId, ambiguousWithoutFraming.ExtractionId);
        Assert.Equal("982d9e3eb996f559e633f4d194def3761d909f5a3b647d1a851fead67c32c9d1", first.TextSha256);
    }

    [Fact]
    public void RejectsInvalidTextAndUnboundedVersions()
    {
        var attempt = Captured("attempt");
        Assert.Throws<ArgumentNullException>(() => new DocumentExtraction(attempt, "p", "n", null!));
        Assert.Throws<ArgumentException>(() => new DocumentExtraction(attempt, "p", "n", "bad\0text"));
        Assert.Throws<ArgumentException>(() => new DocumentExtraction(attempt, "p", "n", "\ud800"));
        Assert.Throws<ArgumentException>(() => new DocumentExtraction(attempt, "p", "n", new string('a', 2_000_001)));
        Assert.Throws<ArgumentException>(() => new DocumentExtraction(attempt, new string('p', 129), "n", ""));
        Assert.Empty(new DocumentExtraction(attempt, "p", "n", "").Text);
    }

    [Fact]
    public void ProfileRevisionChangesIdentityAndLegacyIdentityStaysUnchanged()
    {
        var attempt = Captured("attempt");
        var legacy = new DocumentExtraction(attempt, "parser-v1", "normalization-v1", "text");
        var profile = new DocumentContentProfile("profile", "main", [".navigation"]);
        var configured = new DocumentExtraction(attempt, "parser-v1", "normalization-v1", "text", profile);
        var updatedProfile = new DocumentExtraction(attempt, "parser-v1", "normalization-v1", "text",
            new DocumentContentProfile("profile", "article", [".navigation"]));

        Assert.Null(legacy.Profile);
        Assert.Equal("9a96469ddac3413d432501af5ad40a1df9dd3b85ead274c77f5c1f94f295d42b", legacy.ExtractionId);
        Assert.Same(profile, configured.Profile);
        Assert.NotEqual(legacy.ExtractionId, configured.ExtractionId);
        Assert.NotEqual(configured.ExtractionId, updatedProfile.ExtractionId);
    }

    [Fact]
    public void SpanRetainsExactUnicodeQuoteAndRejectsInvalidBoundaries()
    {
        var extraction = new DocumentExtraction(Captured("attempt"), "p", "n", "A😀B");
        var span = new DocumentTextSpan(extraction, 1, 2);

        Assert.Equal(extraction.ExtractionId, span.ExtractionId);
        Assert.Equal("😀", span.Quote);
        Assert.Throws<ArgumentException>(() => new DocumentTextSpan(extraction, 2, 1));
        Assert.Throws<ArgumentException>(() => new DocumentTextSpan(extraction, 1, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentTextSpan(extraction, 0, 0));
        Assert.Throws<ArgumentException>(() => new DocumentTextSpan(extraction, int.MaxValue, 1));
    }

    private static CapturedAttemptResult Captured(string id) => new(id, "source", "https://example.test/",
        "https://example.test/", DateTimeOffset.UnixEpoch,
        new CollectionResponse(200, null, null, "text/html", []), new CaptureIdentity(new string('a', 64), 0));
}
