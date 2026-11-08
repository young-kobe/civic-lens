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
    public async Task UnknownTotalsStayAbsentButThePublishedResultRemainsVisible()
    {
        var page = await RenderAsync(new(7, Featured with { Yea = null, Nay = null }, [], null));

        Assert.Null(page.QuerySelector(".home-featured .home-vote-counts"));
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
        var summary = page.QuerySelector(".home-featured-summary")!;

        Assert.Null(summary.QuerySelector("script"));
        Assert.Equal(text, summary.QuerySelector("p")!.TextContent);
        Assert.Equal(wording, summary.QuerySelector("span")?.TextContent);
        Assert.Equal(origin == PublishedTextOrigin.Model, summary.ClassList.Contains("home-ai-summary"));
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
        Assert.Equal(4, recent.QuerySelectorAll(".latest-rows > li").Length);
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
        Assert.Equal(4, page.QuerySelectorAll(".latest-rows > li").Length);
    }

    [Fact]
    public async Task ThreeRecentVotesAndTheFeaturedVoteCannotMakeAFourVoteCaption()
    {
        var page = await RenderAsync(new(7, Featured, RecentVotes(PublishedVoteAlignment.Split)[..3], null));

        Assert.Null(page.QuerySelector(".home-vote-caption"));
        Assert.Equal(3, page.QuerySelectorAll(".latest-rows > li").Length);
    }

    private static HomeVoteData[] RecentVotes(PublishedVoteAlignment alignment) =>
        [.. Enumerable.Range(0, 4).Select(index => new HomeVoteData(new string((char)('b' + index), 64), new(2026, 3, 2),
            index < 2 ? PublishedChamber.House : PublishedChamber.Senate, "Published vote", null, "Passed", 216, 214, null, null)
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
