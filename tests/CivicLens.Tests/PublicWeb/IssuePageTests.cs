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
    private static readonly IssueBillData Bill = new(new string('b', 64), "H.R. 1 would set storage rules.", PublishedRecordLabels.ModelWording, new(2026, 2, 1), "Current bill line.", PublishedWording.BillLineSource);

    private static IssuePageData Data(IssueLineUpData? lineUp = null, IssueVoteData[]? votes = null, IssueBillData[]? bills = null) =>
        new(7, "firearms", "Firearms", 3, 186, votes ?? [Vote], bills ?? [Bill], lineUp);

    [Fact]
    public async Task TheCountLineNamesTheWhatTheTagAndTheReleaseAndTheTagCaveatFollowsIt()
    {
        var page = await RenderAsync(Data());

        Assert.Equal("This release holds 3 substantive roll call votes and 186 bills and resolutions tagged Firearms.", page.QuerySelector("[data-issue-counts]")!.TextContent);
        Assert.Contains("An AI model picks each tag and no person checks it", page.QuerySelector(".issue-hero")!.TextContent);
    }

    [Fact]
    public async Task OneVoteAndOneBillUseTheSingularNouns()
    {
        var page = await RenderAsync(Data() with { SubstantiveVotes = 1, BillCount = 1 });

        Assert.Equal("This release holds 1 substantive roll call vote and 1 bill or resolution tagged Firearms.", page.QuerySelector("[data-issue-counts]")!.TextContent);
    }

    [Fact]
    public async Task TheVoteSectionKeepsTheCaveatThatARollCallConcernsItsQuestionNotTheWholeIssue()
    {
        var page = await RenderAsync(Data());

        Assert.Equal("Each vote below is about its own question, not about the whole issue. A vote does not show a position on the issue.", page.QuerySelector("#votes .issue-note")!.TextContent);
        Assert.Contains("52 Yes", page.QuerySelector("#votes .issue-tally")!.TextContent);
    }

    [Fact]
    public async Task ABillKeepsItsPublicAiLabelAndTheSourceOfItsBillLine()
    {
        var page = await RenderAsync(Data());
        var row = page.QuerySelector("#bills .issue-row")!;

        Assert.Equal(PublishedRecordLabels.ModelWording, row.QuerySelector(".issue-label")!.TextContent);
        Assert.Contains("Current bill line. " + PublishedWording.BillLineSource, row.TextContent);
        Assert.Contains("not the day the bill began", row.TextContent);
    }

    [Fact]
    public async Task ARowWithoutALabelShowsNoLabel()
    {
        var page = await RenderAsync(Data(bills: [Bill with { Label = null, BillLine = null, BillLineSource = null }]));

        Assert.Null(page.QuerySelector("#bills .issue-label"));
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
        var parties = new IssueLineUpPartyData[] { new("Republican", 3), new("Democratic", 1) };
        var with = await RenderAsync(Data(new IssueLineUpData(119, 5, parties, 1)));
        var without = await RenderAsync(Data(new IssueLineUpData(119, 4, parties, 0)));

        Assert.Contains("5 bills and resolutions tagged Firearms in the 119th Congress", with.QuerySelector("#parties")!.TextContent);
        Assert.Equal(3, with.QuerySelectorAll("[data-issue-lineup] li").Length);
        Assert.Equal(2, without.QuerySelectorAll("[data-issue-lineup] li").Length);
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
    public async Task TheBreadcrumbTrailRunsHomeIssuesThenTheIssueName()
    {
        var page = await RenderAsync(Data());

        Assert.Equal("Firearms", page.QuerySelector(".public-breadcrumbs [aria-current]")!.TextContent.Trim());
        Assert.Contains("Issues", page.QuerySelector(".public-breadcrumbs")!.TextContent);
    }

    [Fact]
    public async Task TheIssuePagePassagesReadAtOrBelowTheSharedGradeLimit()
    {
        var page = await RenderAsync(Data(new IssueLineUpData(119, 5, [new("Republican", 3), new("Democratic", 1)], 1)));

        foreach (var selector in new[] { "[data-issue-counts]", ".issue-hero .small", "#votes .issue-note", "#parties > p" })
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
