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

    [Fact]
    public async Task CompareBreadcrumbsUseThePublicRootAndMarkTheCurrentPageWithoutALink()
    {
        const string publicRoot = "https://civic-lens.test/";
        const string privateApi = "https://api.civic-lens.test";
        var page = await RenderAsync<ComparePage>(new Dictionary<string, object?>
        {
            [nameof(ComparePage.RootPath)] = publicRoot,
            [nameof(ComparePage.BrowserApiUrl)] = privateApi
        });
        var breadcrumb = Assert.Single(page.QuerySelectorAll("nav[aria-label=Breadcrumb]"));
        var links = breadcrumb.QuerySelectorAll("a");
        var current = Assert.Single(breadcrumb.QuerySelectorAll("[aria-current=page]"));

        Assert.Single(links);
        Assert.Equal(publicRoot + PublicationProtocol.HomePath, links[0].GetAttribute("href"));
        Assert.Equal("Home", links[0].TextContent);
        Assert.Equal("Compare your members", current.TextContent);
        Assert.Equal("span", current.LocalName);
        Assert.Null(current.GetAttribute("href"));
        Assert.Equal(1, breadcrumb.QuerySelectorAll(".public-breadcrumb-separator[aria-hidden=true]").Length);
        Assert.DoesNotContain(privateApi, breadcrumb.InnerHtml, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task BreadcrumbTextIsRazorEncoded()
    {
        var items = new[] { new PublicBreadcrumbItem("A <b>label</b>", "this-page.html") };
        var page = await RenderAsync<PublicBreadcrumbs>(new Dictionary<string, object?>
        {
            [nameof(PublicBreadcrumbs.Items)] = items,
            [nameof(PublicBreadcrumbs.RootPath)] = Root
        });
        var current = Assert.Single(page.QuerySelectorAll("[aria-current=page]"));

        Assert.Equal("A <b>label</b>", current.TextContent);
        Assert.Empty(current.QuerySelectorAll("b"));
        Assert.Equal("span", current.LocalName);
        Assert.Null(current.GetAttribute("href"));
    }

    [Fact]
    public async Task AnInnerPageTrailRunsFromHomeThroughItsSectionToItsOwnTitleAndHomeHasNone()
    {
        var inner = await RenderAsync(section: PublicSection.Officials, title: "Ayanna Pressley");
        var trail = Assert.Single(inner.QuerySelectorAll("main nav[aria-label=Breadcrumb]"));
        var links = trail.QuerySelectorAll("a");

        Assert.Equal([Root + PublicationProtocol.HomePath, Root + PublicationProtocol.OfficialsPagePath], links.Select(link => link.GetAttribute("href")));
        Assert.Equal(["Home", "Officials"], links.Select(link => link.TextContent));
        Assert.Equal("Ayanna Pressley", Assert.Single(trail.QuerySelectorAll("[aria-current=page]")).TextContent);

        var sectionPage = await RenderAsync(section: PublicSection.Officials, title: "Officials");
        Assert.Equal(["Home"], sectionPage.QuerySelectorAll("main nav[aria-label=Breadcrumb] a").Select(link => link.TextContent));

        var home = await RenderAsync(section: null, title: "Civic Lens", isHome: true);
        Assert.Empty(home.QuerySelectorAll("nav[aria-label=Breadcrumb]"));
    }

    [Fact]
    public async Task TheFooterReportLinkNamesTheRecordAndBindsItsExactRevisionAndOtherPagesKeepTheGeneralLink()
    {
        const string title = "House <b>roll</b> call 308";
        var record = new ReportedRecord("rec-1", 3, PublishedRecordKind.Vote, title);
        var page = await RenderAsync(section: PublicSection.AllRecords, title: "Vote", record: record);
        var link = Assert.Single(page.QuerySelectorAll("footer a[href*='report.html']"));

        Assert.Equal(Root + PublicationProtocol.ReportPageLink("rec-1", 3, title), link.GetAttribute("href"));
        Assert.Equal("Report a problem with this vote", link.FirstChild!.TextContent);
        Assert.Equal(": " + title, link.QuerySelector(".visually-hidden")!.TextContent);
        Assert.Empty(link.QuerySelectorAll("b"));

        var general = await RenderAsync(section: null, title: "About");
        var generalLink = Assert.Single(general.QuerySelectorAll("footer a[href*='report.html']"));
        Assert.Equal("Report a problem", generalLink.TextContent);
        Assert.Equal(Root + PublicationProtocol.ReportPagePath, generalLink.GetAttribute("href"));
    }

    private static Task<IDocument> RenderAsync(PublicSection? section = PublicSection.Officials, string title = "Test", bool isHome = false, ReportedRecord? record = null) => RenderAsync<PublicPage>(new Dictionary<string, object?>
    {
        [nameof(PublicPage.Title)] = title,
        [nameof(PublicPage.Description)] = "Test page",
        [nameof(PublicPage.CanonicalPath)] = null,
        [nameof(PublicPage.RootPath)] = Root,
        [nameof(PublicPage.Section)] = section,
        [nameof(PublicPage.IsHome)] = isHome,
        [nameof(PublicPage.Record)] = record
    });

    private static async Task<IDocument> RenderAsync<TComponent>(Dictionary<string, object?> parameters) where TComponent : IComponent
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(parameters));
            return output.ToHtmlString();
        });
        return await new HtmlParser().ParseDocumentAsync(html);
    }
}
