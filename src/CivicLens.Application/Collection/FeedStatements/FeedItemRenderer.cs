using static CivicLens.Application.Collection.RenderedText;

namespace CivicLens.Application.Collection.FeedStatements;

public static class FeedItemRenderer
{
    public static string Render(FeedItem item, string bodyText)
    {
        var lines = new List<string>();
        AddLine(lines, "Feed item guid", item.Guid);
        lines.Add(Line("Link", item.Link));
        AddLine(lines, "Title", item.Title);
        AddLine(lines, "Published", item.PublishedText);
        return string.Join('\n', lines) + "\n\n" + bodyText;
    }
}
