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
    public async Task TheHeaderHasOneLinkForEachKindAndTheStaticPartyBoardUntilTheCutoverAndNoLinkForTheOldAllRecordsList()
    {
        var page = await RenderAsync(section: PublicSection.Boards, title: "Compare the parties", canonicalPath: PublicSections.StaticBoardsPath);
        var nav = page.QuerySelector(".public-nav")!;

        Assert.Equal(["Officials", "Issues", "Votes", "Bills", "Executive actions", "Compare parties"], nav.QuerySelectorAll("a").Select(link => link.TextContent));
        Assert.Equal(
            [PublicationProtocol.OfficialsPagePath, PublicationProtocol.FacetsPath,
             PublicationProtocol.SearchByKindPath(PublishedRecordKind.Vote), PublicationProtocol.SearchByKindPath(PublishedRecordKind.Action),
             PublicationProtocol.SearchByKindPath(PublishedRecordKind.ExecutiveAction), PublicSections.StaticBoardsPath],
            nav.QuerySelectorAll("a").Select(link => link.GetAttribute("href")![Root.Length..]));
        Assert.DoesNotContain(nav.QuerySelectorAll("a"), link => link.GetAttribute("href")!.Contains("all.html"));
        Assert.Equal("Compare parties", Assert.Single(nav.QuerySelectorAll("[aria-current=page]")).TextContent);
        Assert.Equal(["Home"], page.QuerySelectorAll("main nav[aria-label=Breadcrumb] a").Select(link => link.TextContent));
    }

    [Fact]
    public async Task TheServerBoardsPageTrailEndsAtItsSectionWithNoLinkToTheStaticBoard()
    {
        var page = await RenderAsync(section: PublicSection.Boards, title: "Compare the parties", canonicalPath: PublicSections.BoardsPath);

        Assert.Equal(["Home"], page.QuerySelectorAll("main nav[aria-label=Breadcrumb] a").Select(link => link.TextContent));
        Assert.DoesNotContain(PublicSections.StaticBoardsPath, page.QuerySelector("main nav[aria-label=Breadcrumb]")!.InnerHtml);
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
        var trail = new PublicTrail([new("A <b>parent</b>", "parent.html")], "A <b>label</b>");
        var page = await RenderAsync<PublicBreadcrumbs>(new Dictionary<string, object?>
        {
            [nameof(PublicBreadcrumbs.Trail)] = trail,
            [nameof(PublicBreadcrumbs.RootPath)] = Root
        });
        var current = Assert.Single(page.QuerySelectorAll("[aria-current=page]"));

        Assert.Equal("A <b>label</b>", current.TextContent);
        Assert.Equal("A <b>parent</b>", Assert.Single(page.QuerySelectorAll("a")).TextContent);
        Assert.Empty(page.QuerySelectorAll("b"));
        Assert.Equal("span", current.LocalName);
        Assert.Null(current.GetAttribute("href"));
    }

    [Fact]
    public async Task AnInnerPageTrailRunsFromHomeThroughItsSectionToItsOwnTitle()
    {
        var inner = await RenderAsync(section: PublicSection.Officials, title: "Ayanna Pressley", canonicalPath: "officials/ayanna.html");
        var trail = Assert.Single(inner.QuerySelectorAll("main nav[aria-label=Breadcrumb]"));
        var links = trail.QuerySelectorAll("a");

        Assert.Equal([Root + PublicationProtocol.HomePath, Root + PublicationProtocol.OfficialsPagePath], links.Select(link => link.GetAttribute("href")));
        Assert.Equal(["Home", "Officials"], links.Select(link => link.TextContent));
        Assert.Equal("Ayanna Pressley", Assert.Single(trail.QuerySelectorAll("[aria-current=page]")).TextContent);
    }

    [Fact]
    public async Task ASectionIndexPageDropsTheSectionCrumbEvenWhenItsTitleDiffersFromTheSectionLabelAndLaterPagesKeepIt()
    {
        var cases = new (PublicSection Section, string Title, string Path, bool KeepsSection)[]
        {
            (PublicSection.AllRecords, "All published records", PublicationProtocol.AllRecordsPagePath(1), false),
            (PublicSection.AllRecords, "All published records", PublicationProtocol.AllRecordsPagePath(2), true),
            (PublicSection.Issues, "Issues and policy areas", PublicationProtocol.FacetsPath, false),
            (PublicSection.About, "About Civic Lens", PublicationProtocol.AboutPagePath, false)
        };

        foreach (var (section, title, path, keepsSection) in cases)
        {
            var page = await RenderAsync(section: section, title: title, canonicalPath: path);
            var trail = Assert.Single(page.QuerySelectorAll("main nav[aria-label=Breadcrumb]"));

            Assert.Equal(keepsSection ? 2 : 1, trail.QuerySelectorAll("a").Length);
            Assert.DoesNotContain(trail.QuerySelectorAll("a"), link => link.GetAttribute("href") == Root + path);
            Assert.Equal(title, Assert.Single(trail.QuerySelectorAll("[aria-current=page]")).TextContent);
        }
    }

    [Fact]
    public async Task AGlobalNavigationTrailIsHomeAndTheTitleWhenThePageHasNoSection()
    {
        var page = await RenderAsync(section: null, title: "Report a problem", canonicalPath: PublicationProtocol.ReportPagePath);
        var trail = Assert.Single(page.QuerySelectorAll("main nav[aria-label=Breadcrumb]"));

        Assert.Equal(["Home"], trail.QuerySelectorAll("a").Select(link => link.TextContent));
    }

    [Fact]
    public async Task ARecordPageTrailNamesTheSectionOfItsKindAndEndsWithItsShortNameOrItsTitleWhenItHasNone()
    {
        var cases = new (PublishedRecordKind Kind, string Section, string SectionPath)[]
        {
            (PublishedRecordKind.Vote, "Votes", PublicationProtocol.SearchByKindPath(PublishedRecordKind.Vote)),
            (PublishedRecordKind.Action, "Bills", PublicationProtocol.SearchByKindPath(PublishedRecordKind.Action)),
            (PublishedRecordKind.Brief, "Weekly briefs", PublicationProtocol.BriefsPagePath(1))
        };

        foreach (var (kind, section, sectionPath) in cases)
        {
            var named = new ReportedRecord("rec-1", 1, kind, "A long <b>headline</b>", "Short name");
            var trail = Assert.Single((await RenderAsync(PublicSection.AllRecords, "A long <b>headline</b>", "records/rec-1.html", named)).QuerySelectorAll("main nav[aria-label=Breadcrumb]"));

            Assert.Equal(["Home", section], trail.QuerySelectorAll("a").Select(link => link.TextContent));
            Assert.Equal(Root + sectionPath, trail.QuerySelectorAll("a")[1].GetAttribute("href"));
            Assert.Equal("Short name", Assert.Single(trail.QuerySelectorAll("[aria-current=page]")).TextContent);
        }

        var unnamed = new ReportedRecord("rec-2", 1, PublishedRecordKind.ExecutiveAction, "A long headline");
        var fallback = Assert.Single((await RenderAsync(PublicSection.AllRecords, "A long headline", "records/rec-2.html", unnamed)).QuerySelectorAll("main nav[aria-label=Breadcrumb]"));
        Assert.Equal(["Home", "Executive actions"], fallback.QuerySelectorAll("a").Select(link => link.TextContent));
        Assert.Equal("A long headline", Assert.Single(fallback.QuerySelectorAll("[aria-current=page]")).TextContent);
    }

    [Fact]
    public async Task TheRealHomePageHasNoTrail()
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
            (await renderer.RenderComponentAsync<HomePage>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                [nameof(HomePage.RootPath)] = Root,
                [nameof(HomePage.Data)] = new HomePageData(0, null, [], null)
            }))).ToHtmlString());
        var home = await new HtmlParser().ParseDocumentAsync(html);

        Assert.NotNull(home.QuerySelector("main"));
        Assert.Empty(home.QuerySelectorAll("nav[aria-label=Breadcrumb]"));
    }

    [Fact]
    public async Task TheFooterReportLinkNamesTheRecordAndBindsItsExactRevisionAndOtherPagesKeepTheFixedPageLink()
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
        Assert.Equal("Report a problem with this page", generalLink.TextContent);
        Assert.Equal(Root + PublicationProtocol.ReportPagePath, generalLink.GetAttribute("href"));
    }

    [Fact]
    public async Task FooterAndHeaderLinksShareOneMarkupSoTheFooterMarksTheCurrentSectionLikeTheHeader()
    {
        var page = await RenderAsync(section: PublicSection.About, title: "About Civic Lens", canonicalPath: PublicationProtocol.AboutPagePath);
        var current = Assert.Single(page.QuerySelectorAll("footer [aria-current=page]"));

        Assert.Equal("About", current.TextContent);
        Assert.Equal(Root + PublicationProtocol.AboutPagePath, current.GetAttribute("href"));
    }

    [Fact]
    public async Task OnlyAServerPageShowsTheFreshnessLineAndTheIssuesLinkOfThePreview()
    {
        var staticPage = await RenderAsync();
        var serverPage = await RenderFramedAsync(new(new(2026, 5, 31), new(2026, 5, 29)));

        Assert.Null(staticPage.QuerySelector(".public-freshness"));
        Assert.Equal(Root + PublicationProtocol.FacetsPath, IssuesLink(staticPage).GetAttribute("href"));
        Assert.Equal(["Newest published House roll call: 31 May 2026", "Newest published Senate roll call: 29 May 2026"],
            serverPage.QuerySelectorAll(".public-freshness span").Select(span => span.TextContent));
        Assert.Equal(PublicSections.Header.Select(link => Root + (link.Text == "Issues" ? PublicSections.PreviewIssuesPath : link.Path)),
            serverPage.QuerySelectorAll(".public-nav a").Select(link => link.GetAttribute("href")));
    }

    [Fact]
    public async Task TheFreshnessLineOmitsAChamberWithNoPublishedRollCallAndShowsNothingWhenBothAreEmpty()
    {
        var onlyHouse = await RenderFramedAsync(new(new(2026, 5, 31), null));
        var neither = await RenderFramedAsync(new(null, null));

        Assert.Equal("Newest published House roll call: 31 May 2026", Assert.Single(onlyHouse.QuerySelectorAll(".public-freshness span")).TextContent);
        Assert.Null(neither.QuerySelector(".public-freshness"));
    }

    [Fact]
    public async Task APageOnAPublicRouteKeepsTheStaticIssuesLink()
    {
        var page = await RenderFramedAsync(new(null, null), onPublicRoute: true);

        Assert.Equal(Root + PublicationProtocol.FacetsPath, IssuesLink(page).GetAttribute("href"));
    }

    private static IElement IssuesLink(IDocument page) => page.QuerySelectorAll(".public-nav a").Single(link => link.TextContent == "Issues");

    private static Task<IDocument> RenderFramedAsync(ServerPageContext context, bool onPublicRoute = false) => RenderAsync<ServerPageFrame>(new Dictionary<string, object?>
    {
        [nameof(ServerPageFrame.Context)] = context,
        [nameof(ServerPageFrame.Page)] = typeof(PublicPage),
        [nameof(ServerPageFrame.Parameters)] = new Dictionary<string, object>
        {
            [nameof(PublicPage.Title)] = "Test",
            [nameof(PublicPage.Description)] = "Test page",
            [nameof(PublicPage.CanonicalPath)] = "x.html",
            [nameof(PublicPage.RootPath)] = Root,
            [nameof(PublicPage.Section)] = PublicSection.Officials!,
            [nameof(PublicPage.OnPublicRoute)] = onPublicRoute
        }
    });

    private static Task<IDocument> RenderAsync(PublicSection? section = PublicSection.Officials, string title = "Test", string? canonicalPath = null, ReportedRecord? record = null) => RenderAsync<PublicPage>(new Dictionary<string, object?>
    {
        [nameof(PublicPage.Title)] = title,
        [nameof(PublicPage.Description)] = "Test page",
        [nameof(PublicPage.CanonicalPath)] = canonicalPath,
        [nameof(PublicPage.RootPath)] = Root,
        [nameof(PublicPage.Section)] = section,
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
