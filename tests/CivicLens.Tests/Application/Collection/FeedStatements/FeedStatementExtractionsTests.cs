using CivicLens.Application.Collection.FeedStatements;
using CivicLens.Application.Documents;
using CivicLens.Collection.Contracts;
using CivicLens.Core.Collection;
using CivicLens.Core.Documents;
using static CivicLens.Tests.Application.Collection.FeedStatements.FeedItemParserTests;

namespace CivicLens.Tests.Application.Collection.FeedStatements;

public sealed class FeedStatementExtractionsTests
{
    private const string FeedUrl = "https://senator.test/feed";

    private static readonly CapturedAttemptResult Capture = new("attempt-1", "senator", FeedUrl, FeedUrl, DateTimeOffset.UnixEpoch,
        new CollectionResponse(200, null, null, "application/rss+xml", []), new CaptureIdentity(new string('a', 64), 1));

    private static readonly CollectionRequest Request = new()
    {
        JobId = "job",
        SourceId = "senator",
        Mode = CollectionMode.Feed,
        Url = FeedUrl,
        AllowedOrigin = "https://senator.test",
        AllowedPathPrefix = "/news",
        ArtifactDirectory = Path.GetFullPath("captures")
    };

    [Fact]
    public async Task EachExtractionHoldsTheGuidAndLinkAboveTheBodySoTheCitedTextNamesItsArticle()
    {
        var feed = Feed(Item(link: "https://senator.test/news/a", guid: "g-1", title: "Title", pubDate: "Wed, 28 Oct 2026 15:30:00 +0000",
            encoded: "<![CDATA[<p>Body</p>]]>"));

        var extraction = Assert.Single(await ReadAsync(feed)).Extraction;

        Assert.Equal("Feed item guid: g-1\nLink: https://senator.test/news/a\nTitle: Title\nPublished: Wed, 28 Oct 2026 15:30:00 +0000\n\ntext of <p>Body</p>",
            extraction.Text);
        Assert.Equal("feed-item-statement-v1+fake-extraction", extraction.ExtractionVersion);
    }

    [Fact]
    public async Task TheIdentityOfAnItemDependsOnTheCaptureAndItsIndexSoARerunMakesTheSameIds()
    {
        var feed = Feed(Item(link: "https://senator.test/news/a", description: "one"), Item(link: "https://senator.test/news/b", description: "two"));

        var first = await ReadAsync(feed);
        var rerun = await ReadAsync(feed);

        Assert.Equal(first.Select(item => item.Extraction.ExtractionId), rerun.Select(item => item.Extraction.ExtractionId));
        Assert.Equal(2, first.Select(item => item.Extraction.ExtractionId).Distinct().Count());
        Assert.Equal([0, 1], first.Select(item => item.Extraction.Part!.Value).Order());
    }

    [Fact]
    public async Task ALinkOutsideTheAllowedPrefixOrOriginIsRefusedLikeAPageJobWould()
    {
        var feed = Feed(
            Item(link: "https://senator.test/about", description: "outside prefix"),
            Item(link: "https://other.test/news/x", description: "outside origin"),
            Item(link: "https://senator.test/news/ok", description: "inside"));

        var links = (await ReadAsync(feed)).Select(item => item.Link);

        Assert.Equal(["https://senator.test/news/ok"], links);
    }

    [Fact]
    public async Task ALinkIsResolvedAgainstTheFeedAndUpgradedToHttpsSoItMatchesTheUrlOfTheDiscoveryAdmission()
    {
        var feed = Feed(Item(link: "/news/relative", description: "a"), Item(link: "http://senator.test/news/plain", description: "b"));

        var links = (await ReadAsync(feed)).Select(item => item.Link);

        Assert.Equal(["https://senator.test/news/relative", "https://senator.test/news/plain"], links);
    }

    [Fact]
    public async Task TheNewestItemComesFirstAndAnUndatedItemComesLast()
    {
        var feed = Feed(
            Item(link: "https://senator.test/news/undated", description: "u"),
            Item(link: "https://senator.test/news/old", description: "o", pubDate: "Mon, 26 Oct 2026 10:00:00 +0000"),
            Item(link: "https://senator.test/news/new", description: "n", pubDate: "Wed, 28 Oct 2026 10:00:00 +0000"));

        var links = (await ReadAsync(feed)).Select(item => item.Link.Split('/')[^1]);

        Assert.Equal(["new", "old", "undated"], links);
    }

    [Fact]
    public async Task ASecondItemWithTheSameLinkIsDroppedSoOneLinkMakesOneExtraction()
    {
        var feed = Feed(Item(link: "https://senator.test/news/a", description: "one"), Item(link: "https://senator.test/news/a", description: "two"));

        Assert.Single(await ReadAsync(feed));
    }

    [Fact]
    public async Task AnItemWhoseBodyHasNoTextAfterExtractionIsSkippedWithoutAGuess()
    {
        var feed = Feed(Item(link: "https://senator.test/news/a", description: "<img src=x>"));

        Assert.Empty(await FeedStatementExtractions.ReadAsync(feed, Capture, Request, new Extractor(_ => " "), default));
    }

    private static Task<IReadOnlyList<FeedItemExtraction>> ReadAsync(byte[] feed) =>
        FeedStatementExtractions.ReadAsync(feed, Capture, Request, new Extractor(markup => "text of " + markup), default);

    private sealed class Extractor(Func<string, string> fragment) : IDocumentTextExtractor
    {
        public string ExtractionVersion => "fake-extraction";

        public Task<string> ExtractAsync(CapturedAttemptResult attempt, string artifactRoot, DocumentContentProfile? profile,
            CancellationToken cancellationToken) => throw new NotSupportedException();

        public Task<string> ExtractHtmlFragmentAsync(string markup, CancellationToken cancellationToken) =>
            Task.FromResult(fragment(markup));
    }
}
