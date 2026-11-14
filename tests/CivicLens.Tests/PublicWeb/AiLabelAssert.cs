using AngleSharp.Dom;

namespace CivicLens.Tests.PublicWeb;

/// <summary>The check that a page draws an AI label only through the <c>AiLabel</c> component. The page tests of each AI text kind share it.</summary>
internal static class AiLabelAssert
{
    /// <summary>Each element that shows <paramref name="label"/> as its whole text is the <c>AiLabel</c> span. A path that draws the label in its own markup fails.</summary>
    public static void DrawnByAiLabel(IParentNode page, string label)
    {
        var shown = page.QuerySelectorAll("*").Where(element => element.Children.Length == 0 && element.TextContent.Trim() == label).ToArray();

        Assert.NotEmpty(shown);
        Assert.All(shown, element =>
        {
            Assert.True(element.TagName == "SPAN" && element.ClassList.Contains("ai-label"),
                $"The label \"{label}\" is drawn by <{element.TagName.ToLowerInvariant()} class=\"{element.ClassName}\">, not by AiLabel.");
        });
    }
}
