using AngleSharp.Dom;

namespace CivicLens.Tests.Fixtures;

internal static class HtmlText
{
    public static string ShownText(this IElement element)
    {
        var copy = (IElement)element.Clone(true);
        foreach (var popover in copy.QuerySelectorAll(".term-popover, .source-line").ToList())
            popover.Remove();
        return copy.TextContent;
    }
}
