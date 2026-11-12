using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class HomePageTests
{
    private const string Root = "https://civic-lens.test/";
    private static readonly HomeFeaturedVoteData Featured = new(new string('a', 64), new(2026, 3, 3),
        PublishedChamber.House, "Published headline", null, "Published summary", PublishedTextOrigin.Model,
        "Passed", 216, 214, "Current bill line.", PublishedWording.BillLineSource);

    [Fact]
    public async Task TheSkipLinkReachesTheMainLandmarkContainingTheHeadingAndLookup()
    {
        var page = await RenderAsync(new(7, Featured, [], null), "https://api.civic-lens.test");
        var main = Assert.Single(page.QuerySelectorAll("main"));

        Assert.Equal("#" + main.Id, page.QuerySelector("a.skip")!.GetAttribute("href"));
        Assert.Equal("-1", main.GetAttribute("tabindex"));
        Assert.NotNull(main.QuerySelector("h1"));
        Assert.NotNull(main.QuerySelector("input[name=zip]"));
    }

    [Fact]
    public async Task CompareLinkUsesTheConfiguredPublicSiteRootWhileZipLookupUsesTheApiOrigin()
    {
        var page = await RenderAsync(new(7, Featured, [], null), "https://api.civic-lens.test");

        Assert.Equal(Root + "compare.html", page.QuerySelectorAll(".home-browse a")
            .Single(link => link.TextContent == "Compare your members").GetAttribute("href"));
        Assert.Equal("https://api.civic-lens.test", page.QuerySelector("[data-zip-api]")!.GetAttribute("data-zip-api"));
    }

    [Fact]
    public async Task UnknownTotalsStayAbsentButThePublishedResultRemainsVisible()
    {
        var page = await RenderAsync(new(7, Featured with { Yea = null, Nay = null }, [], null));

        Assert.Null(page.QuerySelector(".home-featured .record-counts"));
        Assert.Equal("Passed", page.QuerySelector(".home-featured-result")!.TextContent);
        Assert.Contains("Current bill line. " + PublishedWording.BillLineSource, page.QuerySelector(".home-featured")!.TextContent);
    }

    [Theory]
    [InlineData(PublishedTextOrigin.Model, PublishedRecordLabels.ModelWording)]
    [InlineData(PublishedTextOrigin.Code, PublishedRecordLabels.CodeWording)]
    [InlineData(PublishedTextOrigin.Person, null)]
    public async Task TheSummaryKeepsItsPublishedOriginAndEscapesStoredText(PublishedTextOrigin origin, string? wording)
    {
        const string text = "<script>private()</script>";
        var page = await RenderAsync(new(7, Featured with { TextOrigin = origin, Summary = text }, [], null));
        var summary = page.QuerySelector(".home-featured-summary, .home-featured .ai-summary")!;

        Assert.Null(summary.QuerySelector("script"));
        Assert.Equal(text, summary.QuerySelector("p")!.TextContent);
        Assert.Equal(wording, summary.QuerySelector("span")?.TextContent);
        Assert.Equal(origin == PublishedTextOrigin.Model, summary.ClassList.Contains("ai-summary-ai"));
        var descriptionId = page.QuerySelector(".home-featured h2")!.GetAttribute("aria-describedby");
        if (wording is null)
            Assert.Null(descriptionId);
        else
            Assert.Same(summary.QuerySelector("span"), Assert.Single(page.QuerySelectorAll("#" + descriptionId)));
    }

    [Fact]
    public async Task EmptyOptionalSectionsAreOmittedAndABriefIsOneLinkedLine()
    {
        var empty = await RenderAsync(new(7, null, [], null));
        Assert.Null(empty.QuerySelector(".home-featured"));
        Assert.Null(empty.QuerySelector(".home-preview-section"));
        Assert.Null(empty.QuerySelector(".home-brief-line"));

        var id = new string('b', 64);
        var brief = await RenderAsync(new(7, null, [], new(id, "Published brief", null)));
        var line = brief.QuerySelector(".home-brief-line")!;
        Assert.Contains("This week:", line.TextContent);
        Assert.Equal(Root + PublicationProtocol.RecordPagePath(id), line.QuerySelector("a")!.GetAttribute("href"));
    }

    [Theory]
    [InlineData(12, 7, 4, "12 substantive votes", "7 bills", "4 records in this release")]
    [InlineData(1, 1, 1, "1 substantive vote", "1 bill", "1 record in this release")]
    public async Task IssuesExecutiveGroupsAndFreshnessRenderTheirScopedPublishedFacts(
        int substantiveVotes, int bills, int executiveRecords, string voteCount, string billCount, string executiveCount)
    {
        var issue = new HomeIssueData("health", "Health", 119, substantiveVotes, bills);
        var actionId = new string('c', 64);
        var action = new HomeExecutiveGroupData(PublishedExecutiveActionKind.Notice, executiveRecords, actionId,
            "Newest notice title", new(2026, 5, 31));
        var page = await RenderAsync(new(7, null, [], null)
        {
            Issues = [issue],
            ExecutiveActions = [action],
            NewestHouseRollCall = new(2026, 5, 31),
            NewestSenateRollCall = new(2026, 5, 29)
        });

        var issueCard = Assert.Single(page.QuerySelectorAll(".home-issue-list li"));
        var issueLink = Assert.Single(issueCard.QuerySelectorAll("a.home-issue-tile"));
        Assert.Equal(Root + PublicationProtocol.IssuePagePath("health", 1), issueLink.GetAttribute("href"));
        Assert.Contains(voteCount, issueLink.TextContent);
        Assert.Contains(billCount, issueLink.TextContent);
        Assert.Contains("119th Congress", issueLink.TextContent);

        var actionCard = Assert.Single(page.QuerySelectorAll(".home-executive-list li"));
        Assert.Equal("Presidential notice", actionCard.QuerySelector("p")!.TextContent);
        Assert.Equal(Root + PublicationProtocol.RecordPagePath(actionId), actionCard.QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains("Newest notice title", actionCard.TextContent);
        Assert.Contains(executiveCount, actionCard.TextContent);
        Assert.Contains("Signed 31 May 2026", actionCard.TextContent);

        Assert.Same(page.QuerySelector(".home-freshness"), page.QuerySelector(".home-grid")!.FirstElementChild);
        Assert.Contains("Newest published House roll call: 31 May 2026", page.QuerySelector(".home-freshness")!.TextContent);
        Assert.Contains("Newest published Senate roll call: 29 May 2026", page.QuerySelector(".home-freshness")!.TextContent);
    }

    [Fact]
    public async Task FreshnessOmitsAChamberWithoutPublishedRollCalls()
    {
        var page = await RenderAsync(new(7, null, [], null) { NewestHouseRollCall = new(2026, 5, 31) });

        Assert.Contains("Newest published House roll call: 31 May 2026", page.QuerySelector(".home-freshness")!.TextContent);
        Assert.DoesNotContain("Senate", page.QuerySelector(".home-freshness")!.TextContent);
    }

    [Fact]
    public async Task TheRememberedMembersLinkTargetsTheLookupHeadingInSharedMarkup()
    {
        var page = await RenderAsync(new(7, Featured, [], null), "https://api.civic-lens.test");
        var chip = page.QuerySelector("[data-your-members]")!;
        var heading = page.QuerySelector("[data-zip-results] h2")!;
        var link = chip.QuerySelector("a")!;

        Assert.NotEmpty(heading.Id!);
        Assert.Equal("#" + heading.Id, new Uri(link.GetAttribute("href")!).Fragment);
        Assert.Single(page.QuerySelectorAll("#" + heading.Id));
        Assert.True(chip.HasAttribute("hidden"));
        Assert.Empty(page.QuerySelector("[data-zip-body]")!.Children);
        Assert.Null(page.QuerySelector("input[name=zip]")!.GetAttribute("value"));
        Assert.True(page.QuerySelector("[data-zip-results]")!.ClassList.Contains("zip-member-results"));
        Assert.Equal("District lookup source: U.S. Census Bureau", page.QuerySelector("[data-zip-notes] p")!.TextContent);
    }

    [Fact]
    public async Task TheBriefLeadsTheRecentVotesAndASplitCaptionNamesOnlyThoseFourVotes()
    {
        var votes = RecentVotes(PublishedVoteAlignment.Split);
        var page = await RenderAsync(new(7, Featured, votes, new(new string('f', 64), "Published brief", PublishedWording.ModelBrief)));
        var recent = page.QuerySelector(".home-preview-section")!;
        var caption = Assert.Single(recent.QuerySelectorAll(".home-vote-caption"));

        Assert.Contains("All four votes shown", caption.TextContent);
        Assert.Contains("Among members voting Yes or No", caption.TextContent);
        Assert.Contains("most Democrats and most Republicans chose opposite sides", caption.TextContent);
        Assert.Equal(4, recent.QuerySelectorAll(".record-rows > li").Length);
        Assert.Same(recent, page.QuerySelector(".home-brief-line")!.NextElementSibling);
    }

    [Theory]
    [InlineData(null)]
    [InlineData(PublishedVoteAlignment.Mixed)]
    [InlineData(PublishedVoteAlignment.PartiesAgree)]
    public async Task MissingOrDifferentAlignmentCannotBecomeAnAllSplitClaim(PublishedVoteAlignment? alignment)
    {
        var votes = RecentVotes(PublishedVoteAlignment.Split);
        votes[0] = votes[0] with { Alignment = alignment };
        var page = await RenderAsync(new(7, Featured, votes, null));

        Assert.Null(page.QuerySelector(".home-vote-caption"));
        Assert.Equal(4, page.QuerySelectorAll(".record-rows > li").Length);
    }

    [Fact]
    public async Task ThreeRecentVotesAndTheFeaturedVoteCannotMakeAFourVoteCaption()
    {
        var page = await RenderAsync(new(7, Featured, RecentVotes(PublishedVoteAlignment.Split)[..3], null));

        Assert.Null(page.QuerySelector(".home-vote-caption"));
        Assert.Equal(3, page.QuerySelectorAll(".record-rows > li").Length);
    }

    [Fact]
    public async Task ARecentVoteRowShowsItsLinkMetaCountsResultAndTheBillLineWithItsSource()
    {
        var votes = RecentVotes(PublishedVoteAlignment.Split);
        votes[0] = votes[0] with { Label = "AI label", BillLine = "Current bill line." };
        var rows = (await RenderAsync(new(7, Featured, votes, null))).QuerySelectorAll(".record-rows > li");
        var row = rows[0];

        Assert.Equal("Published vote", row.QuerySelector("a > span")!.TextContent);
        Assert.Contains("AI label", row.QuerySelector("a")!.TextContent);
        Assert.Contains("House, 2 Mar 2026", row.QuerySelector("a")!.TextContent);
        Assert.Equal("216Yes214NoPassed", row.QuerySelector(".record-counts")!.TextContent.Replace(" ", ""));
        Assert.Equal("Current bill line.", row.QuerySelector(".bill-line")!.TextContent);
        Assert.Contains(PublishedWording.BillLineSource, row.TextContent);
        var billParagraph = row.QuerySelector(".bill-line")!.ParentElement!;
        Assert.Equal("Current bill line. " + PublishedWording.BillLineSource, billParagraph.TextContent.Trim());
        Assert.Single(row.QuerySelectorAll("p.small"));
        Assert.Null(rows[1].QuerySelector(".bill-line"));
        Assert.DoesNotContain(PublishedWording.BillLineSource, rows[1].TextContent);
    }

    private static VotePageData FeaturedDetail(params VoteMemberData[] members) => VoteDetailPageFixture.Vote() with { Members = members };

    private static VoteMemberData Seat(string id, string cast, string party, string state, int district) =>
        new(id, id, cast, party, VoteDetailPageFixture.Cite(VoteDetailPageFixture.Lines[4]), state, district);

    private static readonly VoteMemberData[] SeatedMembers = [Seat("Ada", "Aye", "Democrat", "MA", 7), Seat("Ben", "Nay", "Republican", "TX", 1), Seat("Cy", "Not Voting", "Republican", "TX", 2)];

    [Fact]
    public async Task TheFeaturedVoteShowsTheCompactMapItsCountsAndTheLinkToTheFullMap()
    {
        var page = await RenderAsync(new(7, Featured, [], null) { FeaturedVoteDetail = FeaturedDetail(SeatedMembers) });
        var featured = page.QuerySelector(".home-featured")!;

        var map = featured.QuerySelector(".vote-map[data-compact]")!;
        Assert.Equal("Map of the vote. Each shape is one House seat, filled by how its member voted: 1 Yes, 1 No, 1 Not voting, 432 No member in the record.",
            map.QuerySelector(".vote-map-shapes")!.GetAttribute("aria-label"));
        Assert.Equal(435, map.QuerySelectorAll(".vote-map-shapes .vote-map-seat").Length);
        Assert.Null(map.QuerySelector(".vote-map-tiles"));
        Assert.Null(map.QuerySelector("h2, table, [data-vote-map-views], [data-vote-map-colors]"));
        var link = featured.QuerySelector(".home-featured-map-link")!;
        Assert.Equal(VoteMapWords.OpenFullMap, link.TextContent);
        Assert.Equal(Root + PublicationProtocol.VoteMapPagePath(Featured.RecordId), link.GetAttribute("href"));
    }

    [Fact]
    public async Task TheFeaturedVoteShowsOnePartyBarForEachPartyCountOfItsRelease()
    {
        var page = await RenderAsync(new(7, Featured, [], null) { FeaturedVoteDetail = FeaturedDetail(SeatedMembers) });

        var bars = page.QuerySelectorAll(".home-featured-parties .party-bar").ToArray();
        Assert.Equal(["House Democrats", "House Republicans", "House Independents"], bars.Select(bar => bar.QuerySelector("figcaption")!.TextContent));
    }

    [Fact]
    public async Task WithoutTheFeaturedPageDataOrSeatsTheHeroShowsNoMapAndNoMapLink()
    {
        var withoutDetail = await RenderAsync(new(7, Featured, [], null));
        Assert.Null(withoutDetail.QuerySelector(".vote-map"));
        Assert.Null(withoutDetail.QuerySelector(".home-featured-map-link"));
        Assert.Null(withoutDetail.QuerySelector(".home-featured-parties"));

        var otherCongress = await RenderAsync(new(7, Featured, [], null) { FeaturedVoteDetail = FeaturedDetail(SeatedMembers) with { Congress = 118, PartyTallies = null } });
        Assert.Null(otherCongress.QuerySelector(".vote-map"));
        Assert.Null(otherCongress.QuerySelector(".home-featured-map-link"));
        Assert.Null(otherCongress.QuerySelector(".home-featured-parties"));
    }

    [Fact]
    public async Task TheRecentVotesHeadingExplainsSubstantiveInAPopoverAndCountsTheVotesOnlyWhenThereAreFour()
    {
        var four = await RenderAsync(new(7, Featured, RecentVotes(PublishedVoteAlignment.Split), null));
        var heading = four.QuerySelector("#home-recent-title")!;
        Assert.StartsWith("Four recent substantive", heading.TextContent);
        Assert.EndsWith("votes", heading.TextContent.Trim());
        Assert.Equal("substantive", heading.QuerySelector("button.term")!.TextContent);
        Assert.Equal(Glossary.Term("substantive-vote").Means, heading.QuerySelector(".term-means")!.TextContent);
        Assert.Equal("Latest votes in Congress", four.QuerySelector(".home-eyebrow")!.TextContent);
        Assert.StartsWith("Two House votes and two Senate votes", four.QuerySelector(".home-votes .home-section-lede")!.TextContent);

        var three = await RenderAsync(new(7, Featured, RecentVotes(PublishedVoteAlignment.Split)[..3], null));
        Assert.StartsWith("Recent substantive", three.QuerySelector("#home-recent-title")!.TextContent);
        Assert.Null(three.QuerySelector(".home-votes .home-section-lede"));
    }

    private static BoardPanelData Panel(PublishedChamber chamber, string party, int? congress, params (string Name, string? Seat, int Cast, int Breaks)[] rows) =>
        new(chamber, party, congress, null, null, rows.Length > 0,
            [.. rows.Select(row => new BoardRowData(PublicationProtocol.OfficialCongressPagePath(row.Name.ToLowerInvariant(), congress ?? 119), row.Name, row.Seat, row.Cast, row.Breaks))]);

    private static BoardPanelData[] FourPanels(int senateCongress = 119) =>
    [
        Panel(PublishedChamber.House, "Democrat", 119, ("Henry Cuellar", null, 254, 160), ("Second Dem", null, 200, 1)),
        Panel(PublishedChamber.House, "Republican", 119, ("James Moylan", "Delegate for Guam", 51, 26)),
        Panel(PublishedChamber.Senate, "Democrat", senateCongress, ("John Fetterman", null, 337, 66)),
        Panel(PublishedChamber.Senate, "Republican", senateCongress, ("Lisa Murkowski", null, 341, 58))
    ];

    [Fact]
    public async Task TheBreakBoardShowsTheFirstMemberOfEachBoardWithTheirCountsAndLinksToAllFourBoards()
    {
        var page = await RenderAsync(new(7, null, [], null) { Boards = FourPanels() });
        var section = page.QuerySelector(".home-breakers")!;

        Assert.Equal("Who broke from their party most", section.QuerySelector("h2")!.TextContent);
        var cards = section.QuerySelectorAll(".home-breaker-card").ToArray();
        Assert.Equal(["Henry Cuellar", "James Moylan", "John Fetterman", "Lisa Murkowski"], cards.Select(card => card.QuerySelector(".home-breaker-name")!.TextContent));
        Assert.Equal(["160 of 254", "26 of 51", "66 of 337", "58 of 341"], cards.Select(card => card.QuerySelector(".home-breaker-figure")!.TextContent));
        Assert.Equal(["House Democrats", "House Republicans, Delegate for Guam", "Senate Democrats", "Senate Republicans"], cards.Select(card => card.QuerySelectorAll("p")[0].TextContent));
        Assert.Equal(["split votes cast against most Democrats", "split votes cast against most Republicans", "split votes cast against most Democrats", "split votes cast against most Republicans"],
            cards.Select(card => card.QuerySelectorAll("p")[1].TextContent));
        Assert.Equal(Root + PublicationProtocol.OfficialCongressPagePath("henry cuellar", 119), cards[0].QuerySelector("a")!.GetAttribute("href"));
        var lede = section.QuerySelector(".home-section-lede")!;
        Assert.Contains("most often in the 119th Congress. A member needs at least", lede.TextContent);
        Assert.Equal("split vote", lede.QuerySelector("button.term")!.TextContent);
        Assert.Equal(Root + PublicSections.StaticBoardsPath, section.QuerySelectorAll(".home-breakers-more a").Single(link => link.TextContent == "See all four party boards").GetAttribute("href"));
        Assert.Equal(Root + PublicationProtocol.MethodPagePath, section.QuerySelectorAll(".home-breakers-more a").Single(link => link.TextContent == "How we choose these members").GetAttribute("href"));
    }

    [Fact]
    public async Task WhenTheBoardsKeepDifferentCongressesEachCardNamesItsOwn()
    {
        var page = await RenderAsync(new(7, null, [], null) { Boards = FourPanels(118) });

        Assert.DoesNotContain("Congress. A member", page.QuerySelector(".home-breakers .home-section-lede")!.TextContent);
        Assert.Equal(["split votes cast against most Democrats, 119th Congress", "split votes cast against most Republicans, 119th Congress",
            "split votes cast against most Democrats, 118th Congress", "split votes cast against most Republicans, 118th Congress"],
            page.QuerySelectorAll(".home-breaker-card").Select(card => card.QuerySelectorAll("p")[1].TextContent));
    }

    [Fact]
    public async Task ABoardWithoutAMemberOrWithoutFiguresAddsNoCardAndNoBoardsOmitsTheSection()
    {
        var panels = FourPanels();
        panels[1] = Panel(PublishedChamber.House, "Republican", 119);
        panels[2] = Panel(PublishedChamber.Senate, "Democrat", null);
        var page = await RenderAsync(new(7, null, [], null) { Boards = panels });

        Assert.Equal(["Henry Cuellar", "Lisa Murkowski"], page.QuerySelectorAll(".home-breaker-name").Select(name => name.TextContent));
        Assert.Null((await RenderAsync(new(7, null, [], null))).QuerySelector(".home-breakers"));
        Assert.Null((await RenderAsync(new(7, null, [], null) { Boards = [panels[1], panels[2]] })).QuerySelector(".home-breakers"));
    }

    private static HomeVoteData[] RecentVotes(PublishedVoteAlignment alignment) =>
        [.. Enumerable.Range(0, 4).Select(index => new HomeVoteData(new string((char)('b' + index), 64), new(2026, 3, 2),
            index < 2 ? PublishedChamber.House : PublishedChamber.Senate, "Published vote", null, "Passed", 216, 214, null)
        { Alignment = alignment })];

    private static async Task<IDocument> RenderAsync(HomePageData data, string? browserApiUrl = null)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<HomePage>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(HomePage.Data)] = data, [nameof(HomePage.RootPath)] = Root, [nameof(HomePage.BrowserApiUrl)] = browserApiUrl }));
            return output.ToHtmlString();
        });
        return await new HtmlParser().ParseDocumentAsync(html);
    }
}
