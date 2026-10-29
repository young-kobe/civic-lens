using System.Text;
using CivicLens.Application.Collection.FeedStatements;

namespace CivicLens.Tests.Application.Collection.FeedStatements;

public sealed class FeedItemParserTests
{
    [Fact]
    public void ContentEncodedInCdataIsTheBodyAndWinsOverTheShortDescription()
    {
        var items = Parse(Feed(Item(link: "https://x.test/a", guid: "g-1", title: "Title",
            encoded: "<![CDATA[<p>Full statement &amp; more.</p>]]>", description: "Short teaser")));

        var item = Assert.Single(items);
        Assert.Equal("<p>Full statement &amp; more.</p>", item.Html);
        Assert.Equal(("g-1", "Title", "https://x.test/a"), (item.Guid, item.Title, item.Link));
    }

    [Fact]
    public void DescriptionIsTheBodyWhenAnItemHasNoContentEncodedBecauseSomeSenatorsPublishFullTextThereOnly()
    {
        var item = Assert.Single(Parse(Feed(Item(link: "https://x.test/a", description: "<p>Whole text</p>"))));

        Assert.Equal("<p>Whole text</p>", item.Html);
    }

    [Fact]
    public void AnItemWithNoTextIsSkippedAndTheOthersKeepTheirIndexInTheCapture()
    {
        var items = Parse(Feed(
            Item(link: "https://x.test/a", description: "first"),
            Item(link: "https://x.test/b", encoded: "  ", description: "\n"),
            Item(link: "https://x.test/c", description: "third")));

        Assert.Equal([(0, "https://x.test/a"), (2, "https://x.test/c")], items.Select(item => (item.Index, item.Link)));
    }

    [Fact]
    public void AnItemWithNoLinkIsSkippedBecauseItCannotNameAnArticle()
    {
        Assert.Empty(Parse(Feed(Item(description: "text"))));
    }

    [Fact]
    public void ThePublishedTextIsKeptAsWrittenAndParsedForOrdering()
    {
        var item = Assert.Single(Parse(Feed(Item(link: "https://x.test/a", description: "t", pubDate: "Wed, 28 Oct 2026 15:30:00 +0000"))));

        Assert.Equal("Wed, 28 Oct 2026 15:30:00 +0000", item.PublishedText);
        Assert.Equal(new DateTimeOffset(2026, 10, 28, 15, 30, 0, TimeSpan.Zero), item.Published);
    }

    [Fact]
    public void ADocumentOverTheCharacterLimitIsRefusedSoAFeedCannotExhaustMemory()
    {
        var feed = Feed(Item(link: "https://x.test/a", description: new string('a', 10_000_001)));

        Assert.Throws<InvalidDataException>(() => Parse(feed));
    }

    [Fact]
    public void ADocumentTypeDeclarationIsRefusedSoNoEntityCanExpand()
    {
        var feed = Encoding.UTF8.GetBytes("""<?xml version="1.0"?><!DOCTYPE rss [<!ENTITY a "aaaa">]><rss version="2.0"><channel/></rss>""");

        Assert.Throws<InvalidDataException>(() => FeedItemParser.Parse(feed));
    }

    [Fact]
    public void ADocumentThatIsNotRssIsRefused()
    {
        Assert.Throws<InvalidDataException>(() => FeedItemParser.Parse(Encoding.UTF8.GetBytes("<feed/>")));
    }

    private static IReadOnlyList<FeedItem> Parse(byte[] feed) => FeedItemParser.Parse(feed);

    internal static byte[] Feed(params string[] items) => Encoding.UTF8.GetBytes(
        "<?xml version=\"1.0\"?><rss version=\"2.0\" xmlns:content=\"http://purl.org/rss/1.0/modules/content/\"><channel><title>t</title>"
        + string.Concat(items) + "</channel></rss>");

    internal static string Item(string? link = null, string? guid = null, string? title = null, string? encoded = null,
        string? description = null, string? pubDate = null) =>
        "<item>"
        + (link is null ? "" : $"<link>{link}</link>")
        + (guid is null ? "" : $"<guid isPermaLink=\"false\">{guid}</guid>")
        + (title is null ? "" : $"<title>{title}</title>")
        + (pubDate is null ? "" : $"<pubDate>{pubDate}</pubDate>")
        + (encoded is null ? "" : $"<content:encoded>{encoded}</content:encoded>")
        + (description is null ? "" : $"<description>{System.Security.SecurityElement.Escape(description)}</description>")
        + "</item>";
}
