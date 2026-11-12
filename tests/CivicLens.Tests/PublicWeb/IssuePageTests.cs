using AngleSharp.Dom;
using CivicLens.Application.Actions;
using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class IssuePageTests
{
    private const string Root = "https://civic-lens.test/";
    private static readonly IssueVoteData Vote = new(new string('a', 64), new(2026, 3, 3), PublishedChamber.Senate, "Senate passes a firearms bill", null, "Passed", 52, 48);
    private static readonly IssueBillData Bill = new(new string('b', 64), "H.R. 1 would set storage rules.", PublishedRecordLabels.ModelWording, new(2026, 2, 1), "Current bill line.");

    private static IssuePageData Data(IssueLineUpData? lineUp = null, IssueVoteData[]? votes = null, IssueBillData[]? bills = null) =>
        new(7, "firearms", "Firearms", "Federal firearms law and regulation.", 119, 3, 2, 186, votes ?? [Vote], bills ?? [Bill], lineUp);

    [Fact]
    public async Task TheCountLineNamesTheWhatTheTagAndTheReleaseAndTheTagCaveatFollowsIt()
    {
        var page = await RenderAsync(Data());

        Assert.Equal("This release holds 3 substantive roll call votes and 186 bills and resolutions tagged Firearms in the 119th Congress.", page.QuerySelector("[data-issue-counts]")!.TextContent);
        Assert.Contains(PublishedWording.IssueTagCaveat, page.QuerySelector(".issue-hero")!.TextContent);
    }

    [Fact]
    public void TheStaticIssuePageLedeAndTheServerPageShareOneTagCaveat()
    {
        Assert.Contains(PublishedWording.IssueTagCaveat, CivicLens.Host.Publication.PublishedFacetSummaryView.Lede(CivicLens.Application.Publication.FacetKind.Issue));
    }

    [Fact]
    public async Task EveryBillCountNamesBillsAndResolutionsWithOneNounRule()
    {
        var lineUp = new IssueLineUpData(119, 3, [new("Democrat", 2), new("Republican", 1)], 0);
        var page = await RenderAsync(Data(lineUp: lineUp, bills: [Bill]) with { BillCount = 5 });

        Assert.Contains("The 1 newest of 5.", page.QuerySelector("#bills")!.TextContent);
        Assert.Contains("3 bills and resolutions tagged Firearms", page.QuerySelector("#parties")!.TextContent);
        Assert.Equal(["2 Democrat sponsor", "1 Republican sponsor"], page.QuerySelectorAll("#parties .party-bar-legend li").Select(row => Compact(row.TextContent)));
    }

    private static string Compact(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));

    [Fact]
    public async Task OneVoteAndOneBillUseTheSingularNouns()
    {
        var page = await RenderAsync(Data() with { SubstantiveVotes = 1, BillCount = 1 });

        Assert.Equal("This release holds 1 substantive roll call vote and 1 bill or resolution tagged Firearms in the 119th Congress.", page.QuerySelector("[data-issue-counts]")!.TextContent);
    }

    [Fact]
    public async Task TheSplitVoteLineCountsTheSplitVotesOfTheSubstantiveVotesAndIsAbsentWithNoVotes()
    {
        var page = await RenderAsync(Data());
        var none = await RenderAsync(Data() with { SubstantiveVotes = 0, SplitVotes = 0, Votes = [] });

        Assert.StartsWith("2 of those 3 votes were split votes", page.QuerySelector("[data-issue-split]")!.TextContent);
        Assert.Null(none.QuerySelector("[data-issue-split]"));
    }

    [Fact]
    public async Task AReleaseWithNoCongressShowsNoCountLineBecauseTheTimeFrameIsUnknown()
    {
        var page = await RenderAsync(Data() with { Congress = null });

        Assert.Null(page.QuerySelector("[data-issue-counts]"));
        Assert.Null(page.QuerySelector("[data-issue-split]"));
    }

    [Fact]
    public async Task TheSectionTabsLinkTheSectionsAndTheTagsLinkGoesToTheMethodPage()
    {
        var withParties = await RenderAsync(Data(new IssueLineUpData(119, 1, [new("Democrat", 1)], 0)));
        var without = await RenderAsync(Data());

        Assert.Equal(["#your-members", "#votes", "#bills", "#parties"], withParties.QuerySelectorAll(".issue-tabs a").Select(link => link.GetAttribute("href")));
        Assert.Equal(["#your-members", "#votes", "#bills"], without.QuerySelectorAll(".issue-tabs a").Select(link => link.GetAttribute("href")));
        Assert.Equal(Root + "method.html#issue-tags", without.QuerySelector(".issue-hero .small a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task TheVoteSectionKeepsTheCaveatThatARollCallConcernsItsQuestionNotTheWholeIssue()
    {
        var page = await RenderAsync(Data());

        Assert.Equal("Each vote below is about its own question, not about the whole issue. A vote does not show a position on the issue.", page.QuerySelector("#votes .issue-note")!.TextContent);
        Assert.Contains("52 Yes", page.QuerySelector("#votes .record-counts")!.TextContent);
    }

    [Fact]
    public async Task ABillKeepsItsPublicAiLabelAndTheSourceOfItsBillLine()
    {
        var page = await RenderAsync(Data());
        var row = page.QuerySelector("#bills .record-row")!;

        Assert.Contains(PublishedRecordLabels.ModelWording, row.QuerySelector("a")!.TextContent);
        Assert.Contains("Current bill line.", row.QuerySelector(".bill-line")!.TextContent);
        Assert.Contains(PublishedWording.BillLineSource, row.TextContent);
    }

    [Fact]
    public async Task TheDateNoteIsStatedOnceInTheSectionLeadAndNotOnEachBillRow()
    {
        var page = await RenderAsync(Data(bills: [Bill, Bill with { RecordId = new string('c', 64) }]));

        Assert.Equal(1, page.QuerySelectorAll("#bills .issue-note").Length);
        Assert.Contains("not the day the bill began", page.QuerySelector("#bills .issue-note")!.TextContent);
        Assert.DoesNotContain("not the day the bill began", page.QuerySelector("#bills .record-rows")!.TextContent);
        Assert.All(page.QuerySelectorAll("#bills .record-row"), row => Assert.Contains("Latest sponsorship on 1 Feb 2026", row.TextContent));
    }

    [Fact]
    public async Task TheDefinitionOfTheIssueFollowsItsNameAndIsAbsentWhenTheIssueHasNone()
    {
        var with = await RenderAsync(Data());
        var without = await RenderAsync(Data() with { Definition = null });

        Assert.Equal("Federal firearms law and regulation.", with.QuerySelector(".issue-hero h1 + .issue-definition")!.TextContent);
        Assert.Null(without.QuerySelector(".issue-definition"));
    }

    [Fact]
    public async Task AVoteWithNoMemberListCountsShowsItsResultAndNoZeroTally()
    {
        var page = await RenderAsync(Data(votes: [Vote with { Yea = null, Nay = null }]));
        var counts = page.QuerySelector("#votes .record-counts")!;

        Assert.Equal("Passed", counts.TextContent);
        Assert.Empty(counts.QuerySelectorAll("b"));
    }

    [Fact]
    public async Task ARowWithoutALabelShowsNoLabel()
    {
        var page = await RenderAsync(Data(bills: [Bill with { Label = null, BillLine = null }]));

        Assert.Equal(2, page.QuerySelectorAll("#bills .record-row a > span").Length);
        Assert.DoesNotContain(PublishedRecordLabels.ModelWording, page.QuerySelector("#bills")!.TextContent);
        Assert.Null(page.QuerySelector("#bills .bill-line"));
    }

    [Fact]
    public async Task NoLineUpAndAnEmptyLineUpRenderNoPartySectionSoNoZeroShows()
    {
        Assert.Null((await RenderAsync(Data(null))).QuerySelector("#parties"));
        Assert.Null((await RenderAsync(Data(new IssueLineUpData(119, 0, [], 0)))).QuerySelector("#parties"));
    }

    [Fact]
    public async Task TheLineUpListsEachPartyAndTheUnknownCountOnlyWhenItIsAboveZero()
    {
        var parties = new IssueLineUpPartyData[] { new("Republican", 3), new("Democrat", 1) };
        var with = await RenderAsync(Data(new IssueLineUpData(119, 5, parties, 1)));
        var without = await RenderAsync(Data(new IssueLineUpData(119, 4, parties, 0)));

        Assert.Contains("5 bills and resolutions tagged Firearms in the 119th Congress", with.QuerySelector("#parties")!.TextContent);
        Assert.Equal(3, with.QuerySelectorAll("#parties .party-bar-legend li").Length);
        Assert.Equal(2, without.QuerySelectorAll("#parties .party-bar-legend li").Length);
        Assert.Equal("1 Sponsor party not known", Compact(with.QuerySelectorAll("#parties .party-bar-legend li")[2].TextContent));
    }

    [Fact]
    public async Task TheMembersSlotExistsOnlyWhenABrowserApiIsConfiguredAndTheHomePromptStaysForEveryReader()
    {
        var withApi = await RenderAsync(Data(), "https://api.civic-lens.test");
        var withoutApi = await RenderAsync(Data());

        Assert.Equal("firearms", withApi.QuerySelector("[data-issue-members]")!.GetAttribute("data-issue-members"));
        Assert.Null(withoutApi.QuerySelector("[data-issue-members]"));
        Assert.Equal(Root + "index.html#zip-results", withoutApi.QuerySelector("[data-issue-members-prompt] a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task TheHeroNamesTheKindAboveTheTitleAsTheMockupDoes()
    {
        var page = await RenderAsync(Data());

        Assert.Equal("Issue", page.QuerySelector(".issue-hero > .issue-eyebrow")!.TextContent);
        Assert.Equal("Firearms", page.QuerySelector(".issue-eyebrow + h1")!.TextContent);
    }

    [Fact]
    public async Task TheLineUpIsOnePartyBarWhoseSegmentsAddUpToTheBillCount()
    {
        var lineUp = new IssueLineUpData(119, 6, [new("Republican", 3), new("Democrat", 2)], 1);
        var page = await RenderAsync(Data(lineUp: lineUp));

        var bar = Assert.Single(page.QuerySelectorAll("#parties .party-bar"));
        Assert.Equal(["democrat", "republican", "quiet"], bar.QuerySelectorAll("svg.party-bar-chart rect.party-bar-fill").Select(rect => rect.GetAttribute("data-tone")));
        Assert.Equal(["solid", "stripes", "open"], bar.QuerySelectorAll("svg.party-bar-chart rect.party-bar-fill").Select(rect => rect.GetAttribute("data-fill")));
    }

    [Fact]
    public async Task TheBreadcrumbTrailRunsHomeIssuesThenTheIssueName()
    {
        var page = await RenderAsync(Data());

        Assert.Equal("Firearms", page.QuerySelector(".public-breadcrumbs [aria-current]")!.TextContent.Trim());
        Assert.Contains("Issues", page.QuerySelector(".public-breadcrumbs")!.TextContent);
    }

    [Fact]
    public async Task TheIssuePagePassagesReadAtOrBelowTheSharedGradeLimit()
    {
        var page = await RenderAsync(Data(new IssueLineUpData(119, 5, [new("Republican", 3), new("Democrat", 1)], 1)));

        foreach (var selector in new[] { "[data-issue-counts]", ".issue-hero .small", "#votes .issue-note", "#bills .issue-note", "#parties > p" })
        {
            var passage = page.QuerySelector(selector)!;
            var grade = PassageGrade(passage);
            Assert.True(grade <= ReadingGrade.MaximumGrade, $"{selector} reads at grade {ReadingGrade.Format(grade)}.");
        }
    }

    private static double PassageGrade(IElement passage) => ReadingGrade.Of(passage.TextContent);

    private static async Task<IDocument> RenderAsync(IssuePageData data, string? browserApiUrl = null)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<IssuePage>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(IssuePage.Data)] = data, [nameof(IssuePage.RootPath)] = Root, [nameof(IssuePage.BrowserApiUrl)] = browserApiUrl }));
            return output.ToHtmlString();
        });
        return await new HtmlParser().ParseDocumentAsync(html);
    }
}
