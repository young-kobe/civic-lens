using CivicLens.Core.Collection;
using CivicLens.Core.Documents;

namespace CivicLens.Tests.Core.Documents;

public sealed class DocumentExtractionTests
{
    [Fact]
    public void IdentityIsFramedAndIndependentOfTextWhileTextHashTracksContent()
    {
        var attempt = Captured("ab");
        var first = new DocumentExtraction(attempt, "c", "text");
        var changedText = new DocumentExtraction(attempt, "c", "other");
        var ambiguousWithoutFraming = new DocumentExtraction(Captured("a"), "bc", "text");

        Assert.Equal(first.ExtractionId, changedText.ExtractionId);
        Assert.Equal("f2939f903016e5bb29b1e4a61cdbd376220ca03a24180b39995f2d50f2e0a647", first.ExtractionId);
        Assert.NotEqual(first.TextSha256, changedText.TextSha256);
        Assert.NotEqual(first.ExtractionId, ambiguousWithoutFraming.ExtractionId);
        Assert.Equal("982d9e3eb996f559e633f4d194def3761d909f5a3b647d1a851fead67c32c9d1", first.TextSha256);
    }

    [Fact]
    public void APartSplitsOneCaptureIntoOneIdentityEachWhileAnExtractionWithoutAPartKeepsItsOldId()
    {
        var attempt = Captured("ab");
        var whole = new DocumentExtraction(attempt, "c", "text");
        var partOne = new DocumentExtraction(attempt, "c", "text", part: 1);
        var partTwo = new DocumentExtraction(attempt, "c", "text", part: 2);

        Assert.Equal("f2939f903016e5bb29b1e4a61cdbd376220ca03a24180b39995f2d50f2e0a647", whole.ExtractionId);
        Assert.Equal(3, new[] { whole.ExtractionId, partOne.ExtractionId, partTwo.ExtractionId }.Distinct().Count());
        Assert.Throws<ArgumentOutOfRangeException>(() => new DocumentExtraction(attempt, "c", "text", part: -1));
    }

    [Fact]
    public void RejectsInvalidTextAndUnboundedVersions()
    {
        var attempt = Captured("attempt");
        Assert.Throws<ArgumentNullException>(() => new DocumentExtraction(attempt, "p", null!));
        Assert.Throws<ArgumentException>(() => new DocumentExtraction(attempt, "p", "bad\0text"));
        Assert.Throws<ArgumentException>(() => new DocumentExtraction(attempt, "p", "\ud800"));
        Assert.Throws<ArgumentException>(() => new DocumentExtraction(attempt, "p", new string('a', 2_000_001)));
        Assert.Throws<ArgumentException>(() => new DocumentExtraction(attempt, new string('p', 129), ""));
        Assert.Empty(new DocumentExtraction(attempt, "p", "").Text);
    }

    [Fact]
    public void ProfileRevisionChangesIdentityAndIdentityWithoutProfileStaysUnchanged()
    {
        var attempt = Captured("attempt");
        var unprofiled = new DocumentExtraction(attempt, "extraction-v1", "text");
        var profile = new DocumentContentProfile("profile", "main", [".navigation"]);
        var configured = new DocumentExtraction(attempt, "extraction-v1", "text", profile);
        var updatedProfile = new DocumentExtraction(attempt, "extraction-v1", "text",
            new DocumentContentProfile("profile", "article", [".navigation"]));

        Assert.Null(unprofiled.Profile);
        Assert.Equal("0cc7d38206a89e94f907fea82cd37d0f4352afcfad12aee81946f9ad3a83d6ee", unprofiled.ExtractionId);
        Assert.Same(profile, configured.Profile);
        Assert.NotEqual(unprofiled.ExtractionId, configured.ExtractionId);
        Assert.NotEqual(configured.ExtractionId, updatedProfile.ExtractionId);
    }

    [Fact]
    public void SpanRetainsExactUnicodeQuoteAndRejectsInvalidBoundaries()
    {
        var extraction = new DocumentExtraction(Captured("attempt"), "p", "A😀B");
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
