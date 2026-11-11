using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.Application.Actions;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class OfficialProfilePageTests
{
    private const string Root = "https://civic-lens.test/";
    private static readonly string VoteId = new('a', 64);
    private static readonly string BreakId = new('b', 64);
    private static readonly string MissedId = new('c', 64);
    private static readonly string StatementId = new('d', 64);
    private static readonly string BillId = new('e', 64);

    private static OfficialPageData Member() => new(7, "p1", "Ann Able", PublishedOfficeKind.Representative, "House", "MA", 7, "Democrat", null, new(2027, 1, 3), null)
    {
        Attendance = [new(PublishedChamber.House, 119, 5, 675, new(2025, 1, 3), new(2026, 9, 16))],
        PartyLines = [new(PublishedChamber.House, 119, "Democrat", 248, 1, 249, new(2025, 1, 3), new(2026, 9, 16))],
        BillsCongress = 119,
        SponsoredBills = 43,
        Issues = [new("tax", "Federal taxes", 8)],
        Marks = [new(BreakId, new(2026, 9, 16), PublishedChamber.House, 309, "House passes H.R. 9576", false, "Democrat"),
                 new(MissedId, new(2026, 9, 15), PublishedChamber.House, 305, "House passes H.R. 9500", true, null)],
        Votes = [new(VoteId, new(2026, 9, 16), PublishedChamber.House, 314, "House passes S 2403", "Passed", "Aye", "Yes (Aye)")],
        Statements = [new(StatementId, new(2026, 9, 12), "A statement about H.R. 5334", VoteId, "House agrees to Senate changes", new(2026, 9, 16), PublishedChamber.House, 308, "Nay")],
        Bills = [new(BillId, "hr", null, "H.R. 40 would study reparations.", new(2025, 1, 3),
            "Referred to the House Committee on the Judiciary.", new(2025, 1, 3))]
    };

    private static OfficialPageData President() => new(7, "p4", "Eve East", PublishedOfficeKind.Executive, "President", null, null, "Republican", null, null, null)
    {
        Executive = new(3, new(2025, 1, 20), new(2026, 11, 3),
        [
            new(PublishedExecutiveActionKind.ExecutiveOrder, 2, [new(new string('1', 64), "Housing Costs", new(2026, 10, 22)), new(new string('2', 64), "Women's Health", new(2026, 10, 19))]),
            new(PublishedExecutiveActionKind.Proclamation, 1, [new(new string('3', 64), "Seafood Month", new(2026, 10, 22))])
        ])
    };

    [Fact]
    public async Task TheHeaderNamesTheOfficialAndEachFigureNamesWhatItCountsAndItsCongress()
    {
        var page = await RenderAsync<OfficialProfilePage>(Member());
        var figures = page.QuerySelectorAll(".official-figs li").Select(item => item.TextContent).ToArray();

        Assert.Equal("Ann Able", page.QuerySelector("h1")!.TextContent);
        Assert.Contains("Democratic representative for Massachusetts, district 7.", page.QuerySelector(".official-head .official-lede")!.TextContent);
        Assert.Equal(3, figures.Length);
        Assert.Contains("670roll calls voted on, of 675 House roll calls on this site in the 119th Congress.", figures[0]);
        Assert.StartsWith("248of 249 split votes", figures[1]);
        Assert.Contains("cast with most House Democrats, in the 119th Congress.", figures[1]);
        Assert.Contains("43bills and resolutions sponsored in the 119th Congress", figures[2]);
    }

    [Fact]
    public async Task OneScopedPartyFigureAppearsAndNoSecondPartyCountDuplicatesIt()
    {
        var page = await RenderAsync<OfficialProfilePage>(Member());

        Assert.Single(page.QuerySelectorAll(".official-figs li"), item => item.TextContent.Contains("split votes"));
        Assert.DoesNotContain("against most", page.QuerySelector(".official-figs")!.TextContent);
    }

    [Fact]
    public async Task TheStripHoldsOnlyLinksToPartyBreaksAndMissedVotes()
    {
        var page = await RenderAsync<OfficialProfilePage>(Member());
        var marks = page.QuerySelectorAll(".official-strip a");

        Assert.Equal([Root + "records/" + BreakId + ".html", Root + "records/" + MissedId + ".html"], marks.Select(mark => mark.GetAttribute("href")!).ToArray());
        Assert.True(marks[0].ClassList.Contains("against"));
        Assert.True(marks[1].ClassList.Contains("missed"));
        Assert.Contains("Voted against most Democrats", marks[0].TextContent);
        Assert.DoesNotContain(page.QuerySelectorAll(".official-strip a"), mark => mark.GetAttribute("href")!.Contains(VoteId));
        var legend = page.QuerySelector(".official-legend")!.TextContent;
        Assert.Contains("1 vote against most House Democrats", legend);
        Assert.Contains("5 missed House roll calls", legend);
    }

    [Fact]
    public async Task JumpLinksPointAtTheSectionsThatExistAndEveryTargetExists()
    {
        var page = await RenderAsync<OfficialProfilePage>(Member());
        var targets = page.QuerySelectorAll(".official-jump a").Select(link => link.GetAttribute("href")!).ToArray();

        Assert.Equal(["#votes", "#statements", "#bills", "#issues"], targets);
        Assert.All(targets, target => Assert.NotNull(page.QuerySelector(target)));
        Assert.Null(page.QuerySelector(".official-jump p"));
    }

    [Fact]
    public async Task EmptySectionsAreHiddenAndTheLinkedStatementMessageSitsInTheJumpRow()
    {
        var page = await RenderAsync<OfficialProfilePage>(Member() with { Statements = [], Bills = [], Issues = [] });

        Assert.Null(page.QuerySelector("#statements"));
        Assert.Null(page.QuerySelector("#bills"));
        Assert.Null(page.QuerySelector("#issues"));
        Assert.Equal(["#votes"], page.QuerySelectorAll(".official-jump a").Select(link => link.GetAttribute("href")!).ToArray());
        Assert.Equal(OfficialPageText.NoLinks, page.QuerySelector(".official-jump p")!.TextContent);
    }

    [Fact]
    public async Task LinkedStatementsShowTheRelatedVoteWithTheCastAndKeepTheLinkMeaning()
    {
        var page = await RenderAsync<OfficialProfilePage>(Member());
        var statements = page.QuerySelector("#statements")!;

        Assert.Equal("What they said and how they voted", statements.QuerySelector("h2")!.TextContent);
        Assert.Contains("Said on 12 Sep 2026", statements.TextContent);
        Assert.Contains("House roll call 308, 16 Sep 2026: House agrees to Senate changes", statements.QuerySelector(".official-related a")!.TextContent);
        Assert.Contains("No (Nay)", statements.QuerySelector(".official-cast")!.TextContent);
        Assert.Contains(OfficialPageText.LinkMeaning, statements.TextContent);
        Assert.Equal(Root + "method.html", statements.QuerySelector("p.muted a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task BillRowsShowTheIntroducedDateTheOfficialTitleWithoutABillLineAndAStatusBadge()
    {
        var page = await RenderAsync<OfficialProfilePage>(Member());
        var row = page.QuerySelector("#bills .official-rows li")!;

        Assert.Equal("H.R. 40 would study reparations.", row.QuerySelector("a.official-row-title")!.TextContent);
        Assert.Contains("Introduced 3 Jan 2025", row.QuerySelector(".official-meta")!.TextContent);
        Assert.Equal("In committee", row.QuerySelector(".badge")!.TextContent.Trim());
        Assert.Contains("See all 43 bills", page.QuerySelector("#bills .official-more")!.TextContent);
    }

    [Fact]
    public async Task ABillLineReplacesTheOfficialTitleAndAnUnknownActionGetsNoBadge()
    {
        var bill = Member().Bills[0] with { BillLine = "Studies reparations.", LatestAction = "Something odd happened" };
        var page = await RenderAsync<OfficialProfilePage>(Member() with { Bills = [bill] });
        var row = page.QuerySelector("#bills .official-rows li")!;

        Assert.Equal("Studies reparations.", row.QuerySelector("a.official-row-title")!.TextContent);
        Assert.Null(row.QuerySelector(".badge"));
    }

    [Fact]
    public async Task SeeAllLinksLeadToTheFullRecordPageOfTheOfficial()
    {
        var page = await RenderAsync<OfficialProfilePage>(Member());

        Assert.All(page.QuerySelectorAll(".official-more a"), link => Assert.Equal(Root + "officials/p1/record.html", link.GetAttribute("href")));
        Assert.Equal(3, page.QuerySelectorAll(".official-more a").Length);
    }

    [Fact]
    public async Task ThePageUsesTheSharedTrailTheOfficialsSectionAndNoInlineStyles()
    {
        var page = await RenderAsync<OfficialProfilePage>(Member());
        var trail = page.QuerySelector("nav[aria-label=Breadcrumb]")!;

        Assert.Equal(["Home", "Officials"], trail.QuerySelectorAll("a").Select(link => link.TextContent).ToArray());
        Assert.Equal("Ann Able", trail.QuerySelector("[aria-current=page]")!.TextContent);
        Assert.Equal("page", page.QuerySelectorAll(".public-nav a").Single(link => link.TextContent == "Officials").GetAttribute("aria-current"));
        Assert.Empty(page.QuerySelectorAll("[style]"));
    }

    [Fact]
    public async Task AnOfficialWithNoRecordsShowsOneEmptyMessageAndNoSectionOrFigure()
    {
        var empty = new OfficialPageData(7, "p9", "Fay Fox", null, null, null, null, null, null, null, null);
        var page = await RenderAsync<OfficialProfilePage>(empty);

        Assert.Contains(OfficialPageText.NoPageRecords, page.QuerySelector(".official-page")!.TextContent);
        Assert.Empty(page.QuerySelectorAll(".official-section"));
        Assert.Null(page.QuerySelector(".official-figs"));
    }

    [Fact]
    public async Task ThePresidentPageCountsEachTypeLinksItsSectionAndShowsNoSpeechesSection()
    {
        var page = await RenderAsync<PresidentPage>(President());
        var counts = page.QuerySelectorAll(".official-counts a");

        Assert.Equal("President Eve East", page.QuerySelector("h1")!.TextContent);
        Assert.Contains("Civic Lens holds 3 executive actions that the Federal Register lists for this President, signed from 20 Jan 2025 to 3 Nov 2026.", page.QuerySelector(".official-lede")!.TextContent);
        Assert.Equal(["2executive orders signed by Eve East", "1proclamation signed by Eve East"], counts.Select(link => link.TextContent).ToArray());
        Assert.All(counts, link => Assert.NotNull(page.QuerySelector(link.GetAttribute("href")!)));
        Assert.Equal(["Executive orders (2)", "Proclamations (1)"], page.QuerySelectorAll(".official-exec h2").Select(heading => heading.TextContent).ToArray());
        Assert.DoesNotContain("What the President said", page.QuerySelector(".official-page")!.TextContent);
        Assert.DoesNotContain("said", page.QuerySelector(".official-page")!.TextContent.Replace("Women's Health", ""), StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PresidentRowsShowTheTypeTheSignedDateAndLinkToTheRecord()
    {
        var page = await RenderAsync<PresidentPage>(President());
        var row = page.QuerySelector(".official-exec .official-rows li")!;

        Assert.Equal(Root + "records/" + new string('1', 64) + ".html", row.QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains("Executive order", row.QuerySelector(".official-meta")!.TextContent);
        Assert.Contains("Signed 22 Oct 2026", row.QuerySelector(".official-meta")!.TextContent);
        Assert.Equal(Root + "search.html?kind=executiveAction", page.QuerySelector("a.btn")!.GetAttribute("href"));
    }

    [Fact]
    public async Task FixedPageCopyReadsAtOrBelowTheSharedGradeLimit()
    {
        var passages = new[]
        {
            OfficialPageText.LinkMeaning, OfficialPageText.NoLinks, OfficialPageText.ExecutiveBox,
            OfficialPageText.ExecutiveMeaning(PublishedExecutiveActionKind.ExecutiveOrder), OfficialPageText.ExecutiveMeaning(PublishedExecutiveActionKind.Proclamation),
            OfficialPageText.ExecutiveMeaning(PublishedExecutiveActionKind.Notice), OfficialPageText.ExecutiveMeaning(PublishedExecutiveActionKind.Memorandum)
        };

        foreach (var passage in passages)
            Assert.True(ReadingGrade.RoundedPassage([passage]) <= ReadingGrade.MaximumGrade, $"\"{passage}\" reads above the limit.");
    }

    private static async Task<IDocument> RenderAsync<TComponent>(OfficialPageData data) where TComponent : IComponent
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<TComponent>(ParameterView.FromDictionary(new Dictionary<string, object?>
            {
                ["Data"] = data,
                ["RootPath"] = Root
            }));
            return output.ToHtmlString();
        });
        return await new HtmlParser().ParseDocumentAsync(html);
    }
}
