using AngleSharp.Dom;

namespace CivicLens.Tests.PublicWeb;

/// <summary>The checks that a page draws an AI label only through the <c>AiLabel</c> component. The page tests of each AI text kind share them.</summary>
internal static class AiLabelAssert
{
    /// <summary>Each element that shows <paramref name="label"/> as its whole text is the <c>AiLabel</c> span, or the full label inside an <c>AiLabel</c> chip.
    /// A path that draws the label in its own markup fails.</summary>
    public static void DrawnByAiLabel(IParentNode page, string label)
    {
        var shown = page.QuerySelectorAll("*").Where(element => element.Children.Length == 0 && element.TextContent.Trim() == label).ToArray();

        Assert.NotEmpty(shown);
        Assert.All(shown, element =>
        {
            var drawn = element.TagName == "SPAN" && (element.ClassList.Contains("ai-label") || IsChipDetail(element));
            Assert.True(drawn, $"The label \"{label}\" is drawn by <{element.TagName.ToLowerInvariant()} class=\"{element.ClassName}\">, not by AiLabel.");
        });
    }

    /// <summary>The label is the short chip of a list row or a card. Its visible text is hidden from assistive technology, and the full label is its accessible text.</summary>
    public static void IsChip(IElement label, string chipText, string fullLabel)
    {
        Assert.True(label.ClassList.Contains("ai-label-chip"), $"The label <span class=\"{label.ClassName}\"> is not a chip.");
        Assert.Equal("0", label.GetAttribute("tabindex"));
        var visible = label.QuerySelector(".ai-label-chip-text")!;
        Assert.Equal((chipText, "true"), (visible.TextContent, visible.GetAttribute("aria-hidden")));
        Assert.Equal(fullLabel, label.QuerySelector(".ai-label-detail")!.TextContent);
    }

    /// <summary>The full label of an AI label: the whole text of a full label, or the hidden detail of a chip.</summary>
    public static string FullText(IElement label) => (label.QuerySelector(".ai-label-detail") ?? label).TextContent;

    private static bool IsChipDetail(IElement element) =>
        element.ClassList.Contains("ai-label-detail") && element.ParentElement?.ClassList.Contains("ai-label-chip") == true;
}
