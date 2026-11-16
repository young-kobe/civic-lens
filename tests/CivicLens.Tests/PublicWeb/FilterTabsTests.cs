using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Components.Ui;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class FilterTabsTests
{
    [Fact]
    public async Task TabsRenderAsANavOfLinksThatMarksTheSelectedTabAndShowsItsCount()
    {
        var html = await RenderAsync([FilterTab.Link("All", "?all", true, 3), FilterTab.Link("Senators", "?senators", false)]);
        var tabs = new HtmlParser().ParseDocument(html).QuerySelectorAll("nav.segmented > a.segmented-item");

        Assert.Equal(["?all", "?senators"], tabs.Select(tab => tab.GetAttribute("href")));
        Assert.Equal(["true", null], tabs.Select(tab => tab.GetAttribute("aria-current")));
        Assert.Equal("3", tabs[0].QuerySelector(".segmented-count")!.TextContent);
        Assert.Null(tabs[1].QuerySelector(".segmented-count"));
    }

    private static async Task<string> RenderAsync(IReadOnlyList<FilterTab> tabs)
    {
        await using var renderer = new HtmlRenderer(new ServiceCollection().BuildServiceProvider(), NullLoggerFactory.Instance);
        return await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<FilterTabs>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(FilterTabs.Label)] = "Filter", [nameof(FilterTabs.Tabs)] = tabs }));
            return output.ToHtmlString();
        });
    }
}
