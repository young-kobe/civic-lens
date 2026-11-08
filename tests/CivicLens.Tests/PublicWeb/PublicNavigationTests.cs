using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class PublicNavigationTests
{
    private const string Root = "/preview/";

    [Fact]
    public async Task DesktopAndMobileNavigationShareRootedLinksAndCurrentSection()
    {
        var page = await RenderAsync();
        var navs = page.QuerySelectorAll(".public-nav, .public-menu-nav");

        Assert.Equal(2, navs.Length);
        foreach (var nav in navs)
        {
            var links = nav.QuerySelectorAll("a");
            Assert.Equal(PublicSections.Header.Count, links.Length);
            for (var index = 0; index < links.Length; index++)
            {
                var expected = PublicSections.Header[index];
                Assert.Equal(expected.Text, links[index].TextContent);
                Assert.Equal(Root + expected.Path, links[index].GetAttribute("href"));
                Assert.Equal(expected.Section == PublicSection.Officials ? "page" : null, links[index].GetAttribute("aria-current"));
            }
        }
    }

    [Fact]
    public async Task HeaderHasOneSearchFormUniqueIdsAndNativeClosedMenu()
    {
        var page = await RenderAsync();
        var menu = Assert.Single(page.QuerySelectorAll("header.public-header details.public-menu"));

        Assert.Null(menu.GetAttribute("open"));
        Assert.Equal(1, page.QuerySelectorAll("header.public-header form[role=search]").Length);
        var ids = page.QuerySelectorAll("[id]").Select(element => element.Id).ToArray();
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
    }

    private static async Task<IDocument> RenderAsync()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<PublicPage>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(PublicPage.Title)] = "Test",
                [nameof(PublicPage.Description)] = "Test page",
                [nameof(PublicPage.CanonicalPath)] = null,
                [nameof(PublicPage.RootPath)] = Root,
                [nameof(PublicPage.Section)] = PublicSection.Officials
            }));
            return output.ToHtmlString();
        });
        return await new HtmlParser().ParseDocumentAsync(html);
    }
}
