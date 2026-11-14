using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.Host.Publication;
using CivicLens.PublicApi;
using CivicLens.PublicApi.Pages;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Home;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Pages;

/// <summary>The gated About, Method, glossary, report, weekly briefs list, and issues index pages, and the freshness line that the shell shows on every server page.</summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class FixedPageTests(PostgresCollection postgres)
{
    private const string Root = "https://civic-lens.test/";
    private const string ApiOrigin = "https://api.civic-lens.test";
    private const string SiteKey = "1x00000000000000000000AA";
    private static readonly DateTimeOffset PublishedAt = new(2026, 6, 9, 12, 0, 0, TimeSpan.Zero);

    private Task<ApiHost> StartAsync(params SeededVote[] votes) => StartAsync(options => options, votes);

    private Task<ApiHost> StartWithReportAsync(string? browserApi, string? siteKey) => StartAsync(options => options with
    {
        BrowserApiUrl = PublicBaseUrl.Parse(browserApi, PublicApiOptions.BrowserApiUrlVariable),
        TurnstileSiteKey = siteKey
    });

    private async Task<ApiHost> StartAsync(Func<PublicApiOptions, PublicApiOptions> change, params SeededVote[] votes)
    {
        var host = await ApiHost.StartAsync(postgres, options => change(options with { PublicBaseUrl = PublicBaseUrl.Parse(Root, "test") }));
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 4, PublishedAt);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, votes);
        return host;
    }

    private static async Task<(HttpStatusCode Status, IDocument Page)> GetAsync(ApiHost host, string path)
    {
        using var response = await host.Client.GetAsync(ServerPageEndpoints.Prefix + (path.Length == 0 ? string.Empty : "/" + path));
        return (response.StatusCode, new HtmlParser().ParseDocument(await response.Content.ReadAsStringAsync()));
    }

    /// <summary>The text of the page body without the freshness line, which only a server page shows.</summary>
    private static string MainText(IDocument page)
    {
        page.QuerySelector(".public-freshness")?.Remove();
        return page.QuerySelector("main")!.TextContent;
    }

    private static string Freshness(IDocument page) => string.Join(" | ", page.QuerySelectorAll(".public-freshness span").Select(span => span.TextContent));

    [Fact]
    public async Task AboutShowsItsApprovedTextWithoutTheBillTextSentenceAndRootedLinks()
    {
        await using var host = await StartAsync();

        var (status, page) = await GetAsync(host, PublicationProtocol.AboutPagePath);
        var text = page.QuerySelector("main")!.TextContent;

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(Root + PublicationProtocol.AboutPagePath, page.QuerySelector("link[rel=canonical]")!.GetAttribute("href"));
        Assert.Equal("About Civic Lens", page.QuerySelector("h1")!.TextContent);
        Assert.DoesNotContain("bill text", text, StringComparison.Ordinal);
        Assert.Contains("Civic Lens is not part of any government office, party, campaign or company.", text);
        Assert.Contains($"within {PublishedCountRules.StatementDateWindowDays} days before Civic Lens saved the page", text);
        Assert.Contains(PublishedRecordLabels.ContactEmail, text);
        foreach (var path in new[] { PublicationProtocol.BriefsPagePath(1), PublicationProtocol.ReportPagePath, PublicationProtocol.MethodPagePath })
            Assert.Contains(page.QuerySelectorAll("main a"), link => link.GetAttribute("href") == Root + path);
    }

    [Fact]
    public async Task MethodReadsTheLinkSentenceFromItsOneOwnerAndTheCountsFromTheSharedRulesAndHasNoClosestVoteText()
    {
        await using var host = await StartAsync();

        var (status, page) = await GetAsync(host, PublicationProtocol.MethodPagePath);
        var text = page.QuerySelector("main")!.TextContent;

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("How the counts work", page.QuerySelector("h1")!.TextContent);
        Assert.Contains(OfficialPageText.LinkMeaning, page.QuerySelector("#panel-what-a-link-is")!.Closest("section")!.TextContent);
        Assert.Contains(VoteWords.MissedVoteMeaning, text);
        Assert.Contains($"on at least {PublishedCountRules.MinimumRecordsForShare} split votes", text);
        Assert.Contains("Code picks the items on the home page from the published records. No model picks them.", text);
        Assert.DoesNotContain("closest vote", text, StringComparison.OrdinalIgnoreCase);
        Assert.NotNull(page.QuerySelector("#" + BoardWords.AgreementId));
        Assert.Contains(page.QuerySelectorAll("main a"), link => link.GetAttribute("href") == Root + PublicationProtocol.OfficialsPagePath);
    }

    [Fact]
    public async Task TheGlossaryIsServedInTheServerShellWithTheTextOfTheStaticGlossaryAndItsCanonicalPath()
    {
        await using var host = await StartAsync();
        var staticPage = new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null, PublicBaseUrl.Parse(Root, "test")).RenderGlossaryAsync(CancellationToken.None));

        var (status, page) = await GetAsync(host, PublicationProtocol.GlossaryPagePath);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(Root + PublicationProtocol.GlossaryPagePath, page.QuerySelector("link[rel=canonical]")!.GetAttribute("href"));
        Assert.Equal("Glossary", page.QuerySelector("h1")!.TextContent);
        Assert.Equal(MainText(staticPage), MainText(page));
        Assert.Equal(PublicSections.PreviewIssuesPath, page.QuerySelectorAll(".public-nav a").Single(link => link.TextContent == "Issues").GetAttribute("href"));
        Assert.Equal("page", page.QuerySelectorAll(".public-footer a").Single(link => link.GetAttribute("href") == Root + PublicationProtocol.GlossaryPagePath).GetAttribute("aria-current"));
        Assert.All(page.QuerySelectorAll("link[rel=stylesheet]"), link => Assert.StartsWith(Root, link.GetAttribute("href")));
    }

    [Fact]
    public async Task TheReportPageIsServedInTheServerShellWithTheTextOfTheStaticReportPageAndRootedLinks()
    {
        await using var host = await StartWithReportAsync(ApiOrigin, SiteKey);
        var staticPage = new HtmlParser().ParseDocument(await new ReleaseRenderer(ApiOrigin, SiteKey, PublicBaseUrl.Parse(Root, "test")).RenderReportAsync(CancellationToken.None));

        var (status, page) = await GetAsync(host, PublicationProtocol.ReportPagePath);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(Root + PublicationProtocol.ReportPagePath, page.QuerySelector("link[rel=canonical]")!.GetAttribute("href"));
        Assert.Equal("noindex", page.QuerySelector("meta[name=robots]")!.GetAttribute("content"));
        Assert.Equal("Report a problem", page.QuerySelector("h1")!.TextContent);
        Assert.Equal(MainText(staticPage), MainText(page));
        Assert.Equal(PublicSections.PreviewIssuesPath, page.QuerySelectorAll(".public-nav a").Single(link => link.TextContent == "Issues").GetAttribute("href"));
        Assert.Equal(Root + PublicationProtocol.RecordPagePath("{id}"), page.QuerySelector("form[data-report-api]")!.GetAttribute("data-record-page"));
        Assert.All(page.QuerySelectorAll("main a[href]:not([href^='mailto:'])"), link => Assert.StartsWith(Root, link.GetAttribute("href")));
        foreach (var path in new[] { PublicationProtocol.SearchPagePath, PublicationProtocol.OfficialsPagePath, PublicationProtocol.AllRecordsPagePath(1) })
            Assert.Contains(page.QuerySelectorAll("main a"), link => link.GetAttribute("href") == Root + path);
    }

    [Fact]
    public async Task TheReportFormPostsToTheBrowserApiOriginWithTheSiteKeyThatPublishAlsoReads()
    {
        await using var host = await StartWithReportAsync(ApiOrigin + "/", SiteKey);

        var (_, page) = await GetAsync(host, PublicationProtocol.ReportPagePath);

        Assert.Equal(ApiOrigin + ReaderReportProtocol.SubmitPath, page.QuerySelector("form[data-report-api]")!.GetAttribute("data-report-api"));
        Assert.Equal(SiteKey, page.QuerySelector(".cf-turnstile")!.GetAttribute("data-sitekey"));
        Assert.Single(page.QuerySelectorAll("script[src^='https://challenges.cloudflare.com/turnstile/']"));
        Assert.DoesNotContain("Reporting is not available", page.QuerySelector("main")!.TextContent);
    }

    [Theory]
    [InlineData(null, SiteKey)]
    [InlineData(ApiOrigin, null)]
    [InlineData(null, null)]
    public async Task WithoutTheBrowserApiOriginOrTheSiteKeyTheReportPageSaysReportingIsNotAvailable(string? browserApi, string? siteKey)
    {
        await using var host = await StartWithReportAsync(browserApi, siteKey);

        var (status, page) = await GetAsync(host, PublicationProtocol.ReportPagePath);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("Reporting is not available", page.QuerySelector(".empty h2")!.TextContent);
        Assert.Null(page.QuerySelector("main form"));
        Assert.Null(page.QuerySelector(".cf-turnstile"));
        Assert.Empty(page.QuerySelectorAll("script[src^='https://challenges.cloudflare.com/']"));
        Assert.Equal(Root + PublicationProtocol.AllRecordsPagePath(1), page.QuerySelector(".empty a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task TheBriefsListShowsTheEmptyStateWithALinkToTheVotesListWhenNoBriefIsPublished()
    {
        await using var host = await StartAsync();

        var (status, page) = await GetAsync(host, PublicationProtocol.BriefsPagePath(1));

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal("No weekly briefs yet", page.QuerySelector(".empty h2")!.TextContent);
        var action = Assert.Single(page.QuerySelectorAll(".empty a"));
        Assert.Equal(PublicSections.ListOf(PublishedRecordKind.Vote).Href, action.GetAttribute("href"));
        Assert.Equal("Votes", action.TextContent.Trim());
        Assert.Equal(["H1", "H2"], page.QuerySelectorAll("main h1, main h2, main h3").Select(heading => heading.TagName));
        Assert.Contains("Release 4, published 9 Jun 2026, 12:00 UTC.", page.QuerySelector(".page-header-lede")!.TextContent);
    }

    [Fact]
    public async Task TheBriefsListLinksEachBriefPageNewestWeekFirstWithTheLabelOfTheView()
    {
        await using var host = await StartAsync();
        var older = new SeededBrief('a', "2026-01-05");
        var newer = new SeededBrief('b', "2026-01-12") { TextOrigin = "Person" };
        await HomeSeed.AddBriefsAsync(host.OperationalDatabase, older, newer);

        var (_, page) = await GetAsync(host, PublicationProtocol.BriefsPagePath(1));
        var rows = page.QuerySelectorAll(".record-row");

        Assert.Equal([Root + PublicationProtocol.RecordPagePath(newer.RecordId), Root + PublicationProtocol.RecordPagePath(older.RecordId)],
            rows.Select(row => row.QuerySelector("a")!.GetAttribute("href")));
        Assert.Contains("Week of 12 Jan 2026", rows[0].TextContent);
        Assert.DoesNotContain("AI model", rows[0].TextContent);
        Assert.Contains("Drafted by an AI model from reviewed records. A person checked it before publication.", rows[1].TextContent);
        Assert.Null(page.QuerySelector(".pager"));
    }

    [Fact]
    public async Task TheBriefsListPagesByFiftyAndAPastThePageAnswers404()
    {
        await using var host = await StartAsync();
        var briefs = Enumerable.Range(0, PublicationProtocol.PageSize + 1)
            .Select(index => new SeededBrief((char)(0x100 + index), new DateOnly(2026, 1, 5).AddDays(index * 7).ToString("yyyy-MM-dd")) { RecordId = index.ToString("x64") }).ToArray();
        await HomeSeed.AddBriefsAsync(host.OperationalDatabase, briefs);

        var (_, first) = await GetAsync(host, PublicationProtocol.BriefsPagePath(1));
        var (secondStatus, second) = await GetAsync(host, PublicationProtocol.BriefsPagePath(2));
        var (thirdStatus, _) = await GetAsync(host, PublicationProtocol.BriefsPagePath(3));

        Assert.Equal(PublicationProtocol.PageSize, first.QuerySelectorAll(".record-row").Length);
        Assert.Equal(Root + PublicationProtocol.BriefsPagePath(2), first.QuerySelector(".pager a[rel=next]")!.GetAttribute("href"));
        Assert.Equal(HttpStatusCode.OK, secondStatus);
        Assert.Single(second.QuerySelectorAll(".record-row"));
        Assert.Equal(Root + PublicationProtocol.BriefsPagePath(2), second.QuerySelector("link[rel=canonical]")!.GetAttribute("href"));
        Assert.Equal(Root + PublicationProtocol.BriefsPagePath(1), second.QuerySelector(".pager a[rel=prev]")!.GetAttribute("href"));
        Assert.Equal(HttpStatusCode.NotFound, thirdStatus);
    }

    [Theory]
    [InlineData(50_000_000)]
    [InlineData(int.MaxValue)]
    public async Task ABriefsPageNumberThatOverflowsTheSkipAnswers404(int number)
    {
        await using var host = await StartAsync();

        var (status, _) = await GetAsync(host, PublicationProtocol.BriefsPagePath(number));

        Assert.Equal(HttpStatusCode.NotFound, status);
    }

    [Fact]
    public async Task TheIssuesIndexListsEachIssueByNameWithItsCountsAndLinksItsPage()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica; INSERT INTO release_issues (release_number, issue_id, name, definition, count_congress, substantive_votes, bills) " +
            "VALUES (4, 'tax', 'Taxes <b>', 'd', 119, 1, 3), (4, 'air', 'Air', 'd', NULL, NULL, NULL);");

        var (status, page) = await GetAsync(host, PublicationProtocol.FacetsPath);
        var tiles = page.QuerySelectorAll("a.home-issue-tile");

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Equal(Root + PublicationProtocol.FacetsPath, page.QuerySelector("link[rel=canonical]")!.GetAttribute("href"));
        Assert.Equal("Issues", page.QuerySelector("h1")!.TextContent);
        Assert.Equal("Each page lists the published records that name the issue.", page.QuerySelector(".page-header-lede")!.TextContent.Trim());
        Assert.Equal(["Air", "Taxes <b>"], tiles.Select(tile => tile.QuerySelector(".home-issue-name")!.TextContent));
        Assert.Equal(Root + PublicationProtocol.IssuePagePath("tax", 1), tiles[1].GetAttribute("href"));
        Assert.Contains("1 substantive vote", tiles[1].TextContent);
        Assert.Contains("3 bills", tiles[1].TextContent);
        Assert.Contains("119th Congress", tiles[1].TextContent);
        Assert.Equal("Air", tiles[0].TextContent.Trim());
        Assert.Null(page.QuerySelector(".home-issue-list")!.Closest(".panel"));
        Assert.Equal(PublicSections.PreviewIssuesPath, page.QuerySelectorAll(".public-nav a").Single(link => link.TextContent == "Issues").GetAttribute("href"));
    }

    [Fact]
    public async Task TheIssuesIndexShowsTheEmptyStateWhenTheReleaseHoldsNoIssue()
    {
        await using var host = await StartAsync();

        var (status, page) = await GetAsync(host, PublicationProtocol.FacetsPath);

        Assert.Equal(HttpStatusCode.OK, status);
        Assert.Empty(page.QuerySelectorAll("a.home-issue-tile"));
        Assert.Equal("None yet", page.QuerySelector(".empty h2")!.TextContent);
        Assert.Equal(["H1", "H2"], page.QuerySelectorAll("main h1, main h2, main h3").Select(heading => heading.TagName));
    }

    [Theory]
    [InlineData("")]
    [InlineData(PublicationProtocol.AboutPagePath)]
    [InlineData(PublicationProtocol.MethodPagePath)]
    [InlineData(PublicationProtocol.FacetsPath)]
    [InlineData(PublicationProtocol.VoteKindsPagePath)]
    [InlineData(PublicationProtocol.GlossaryPagePath)]
    [InlineData(PublicationProtocol.ReportPagePath)]
    [InlineData("compare")]
    [InlineData("no-such-page")]
    public async Task EveryServerPageShowsTheNewestRollCallOfEachChamberFromOneOwner(string path)
    {
        await using var host = await StartAsync(new SeededVote('a', "2026-03-03"), new SeededVote('b', "2026-03-09") { RollCall = 2 },
            new SeededVote('c', "2026-02-10") { Chamber = "Senate" });

        var (_, page) = await GetAsync(host, path);

        Assert.Equal("Newest published House roll call: 9 Mar 2026 | Newest published Senate roll call: 10 Feb 2026", Freshness(page));
    }

    [Fact]
    public async Task TheFreshnessLineNamesOnlyAChamberWithAPublishedRollCall()
    {
        await using var host = await StartAsync(new SeededVote('a', "2026-03-03"));

        var (_, page) = await GetAsync(host, PublicationProtocol.AboutPagePath);

        Assert.Equal("Newest published House roll call: 3 Mar 2026", Freshness(page));
    }

    [Fact]
    public async Task AReleaseWithNoPublishedRollCallShowsNoFreshnessLine()
    {
        await using var host = await StartAsync();

        var (_, page) = await GetAsync(host, PublicationProtocol.AboutPagePath);

        Assert.Null(page.QuerySelector(".public-freshness"));
    }
}
