using System.Globalization;
using System.Xml.Linq;

namespace CivicLens.Application.Collection.FeedStatements;

public static class FeedItemParser
{
    private static readonly XNamespace Content = "http://purl.org/rss/1.0/modules/content/";

    public static IReadOnlyList<FeedItem> Parse(ReadOnlySpan<byte> xml)
    {
        var root = BoundedXml.LoadRoot(xml, "Feed", "rss");
        var elements = root.Elements("channel").Elements("item").ToArray();
        var items = new List<FeedItem>();
        for (var index = 0; index < elements.Length; index++)
            if (ItemOf(index, elements[index]) is { } item) items.Add(item);
        return items;
    }

    private static FeedItem? ItemOf(int index, XElement item)
    {
        var link = TextOf(item.Element("link"));
        var html = TextOf(item.Element(Content + "encoded")) ?? TextOf(item.Element("description"));
        if (link is null || html is null) return null;
        var publishedText = TextOf(item.Element("pubDate"));
        return new FeedItem(index, link, TextOf(item.Element("guid")), TextOf(item.Element("title")), publishedText,
            DateTimeOffset.TryParse(publishedText, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal, out var published)
                ? published
                : null,
            html);
    }

    private static string? TextOf(XElement? element) =>
        element?.Value.Trim() is { Length: > 0 } text ? text : null;
}
