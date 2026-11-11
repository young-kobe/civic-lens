using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using Microsoft.AspNetCore.Components;
using Microsoft.AspNetCore.Components.Web;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

namespace CivicLens.Tests.PublicWeb;

public sealed class BillDetailPageTests
{
    private const string Root = "https://civic-lens.test/";
    private static readonly string RecordId = new('a', 64);
    private static readonly BillSponsorData Sponsor = new("p1", "Pat One", "Democrat", new(2025, 9, 11));

    private static readonly BillPageData Bill = new(7, RecordId, 2, 119, "H.R. 5334", "The Example Act of 2026", "H.R. 5334 headline", "House",
        "The bill would add a tax credit.", PublishedTextOrigin.Model, "AI summary. Drafted by an AI model from reviewed records.", new(2025, 9, 11))
    {
        Sponsors = [Sponsor],
        Cosponsors = new("House",
            [new("p2", "Ann Two", "Democrat", new(2025, 9, 11)), new("p3", "Bob Three", "Republican", new(2025, 9, 11))],
            [new("p4", "Cy Four", "Democrat", new(2025, 10, 2)), new("p5", "Di Five", null, new(2025, 10, 3))]),
        Steps = [new("Introduced in the House", "11 Sep 2025, by Pat One"), new("Became law", "18 Sep 2026, Public Law 119-111")],
        PublicLaw = "119-111",
        BecameLawOn = new(2026, 9, 18),
        Status = "Became law: Public Law 119-111.",
        StatusDate = new(2026, 9, 18),
        StatusReadAtUtc = new(2026, 11, 5, 9, 57, 0, TimeSpan.Zero),
        CrsText = "This bill imposes sanctions.\nIt extends a law.",
        CrsVersion = "55",
        Issues = [new("foreign-policy", "Foreign policy and war")],
        PolicyArea = "International Affairs",
        Votes = [new(new string('b', 64), PublishedChamber.House, new(2026, 9, 16), "House agrees to Senate changes", "Passed", 262, 159, 12, PublishedVoteAlignment.Split)]
    };

    [Fact]
    public async Task TheHeadingNamesTheBillAndItsOfficialTitleAndTheTrailEndsWithTheBillNumber()
    {
        var page = await RenderAsync(Bill);

        Assert.Equal("H.R. 5334, The Example Act of 2026", page.QuerySelector("main h1")!.TextContent);
        var trail = page.QuerySelectorAll(".public-breadcrumbs li").Select(item => (item.QuerySelector("a") ?? item).TextContent.Trim()).ToArray();
        Assert.Equal(["Home", "Bills", "H.R. 5334"], trail);
        Assert.Contains("Bill in the 119th Congress | Became law", page.QuerySelector(".bill-eyebrow")!.TextContent);
        Assert.Equal("Sponsored by Pat One, a Democrat. Introduced 11 Sep 2025. Became law on 18 Sep 2026 as Public Law 119-111.", page.QuerySelector(".bill-head .bill-lede")!.TextContent);
    }

    [Fact]
    public async Task TheReportLinkInTheFooterNamesTheBillRecordAndItsRevision()
    {
        var page = await RenderAsync(Bill);

        var link = page.QuerySelectorAll(".public-footer-nav a").Single(item => item.TextContent.StartsWith("Report a problem with this bill", StringComparison.Ordinal));
        Assert.Contains(RecordId, link.GetAttribute("href"));
    }

    [Fact]
    public async Task TheStepperListsOnlyTheReachedStepsAndMarksTheLastAsCurrent()
    {
        var page = await RenderAsync(Bill);
        var steps = page.QuerySelectorAll(".bill-stepper li").ToArray();

        Assert.Equal(["Introduced in the House", "Became law"], steps.Select(step => step.QuerySelector("b")!.TextContent));
        Assert.Equal([null, "step"], steps.Select(step => step.GetAttribute("aria-current")));
        Assert.Null((await RenderAsync(Bill with { Steps = [] })).QuerySelector(".bill-stepper"));
    }

    [Fact]
    public async Task TheLegendUsesTheApprovedGlossaryTextAndLinksToTheGlossary()
    {
        var page = await RenderAsync(Bill);
        var legend = page.QuerySelector(".bill-legend")!;

        Assert.Contains(Glossary.Term("bill").WhyItMatters, legend.TextContent);
        Assert.Equal(Root + "glossary.html#glossary-bill", legend.QuerySelector("a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task TheCrsFoldSitsDirectlyUnderTheLabeledSummaryAndHoldsOneParagraphPerLine()
    {
        var page = await RenderAsync(Bill);
        var summary = page.QuerySelector(".bill-summary")!;
        var fold = page.QuerySelector(".bill-main > details.bill-fold")!;

        Assert.Same(fold, summary.NextElementSibling);
        Assert.True(summary.ClassList.Contains("bill-ai"));
        Assert.Equal(Bill.Label, summary.QuerySelector(".bill-tag")!.TextContent);
        Assert.Equal(["This bill imposes sanctions.", "It extends a law."], fold.QuerySelectorAll("p").Select(paragraph => paragraph.TextContent));
        Assert.Null((await RenderAsync(Bill with { CrsText = null })).QuerySelector(".bill-main > details.bill-fold"));
    }

    [Fact]
    public async Task OnlyAModelSummaryGetsTheAiStyle()
    {
        var page = await RenderAsync(Bill with { TextOrigin = PublishedTextOrigin.Code, Label = null });

        Assert.False(page.QuerySelector(".bill-summary")!.ClassList.Contains("bill-ai"));
        Assert.Equal(PublishedRecordLabels.CodeWording, page.QuerySelector(".bill-tag")!.TextContent);
    }

    [Fact]
    public async Task TheCosponsorSplitNamesTheChamberTheBaseAndEachPartyCount()
    {
        var page = await RenderAsync(Bill);
        var section = page.QuerySelector("#bill-backers-title")!.ParentElement!;

        Assert.Contains("This site tracks 4 House members who cosponsored it: 2 on the day it was introduced and 2 who joined later.", section.QuerySelector(".bill-lede")!.TextContent);
        Assert.Equal(["2 Democrats", "1 No party listed", "1 Republicans"], section.QuerySelectorAll(".bill-party li").Select(item => item.TextContent.Trim()));
        Assert.Contains("Cy Four (2 Oct 2025)", section.QuerySelector("dd:last-of-type")!.TextContent);
    }

    [Fact]
    public async Task LinkedVotesShowTheirTotalsAndASplitBadgeOnlyForASplitVote()
    {
        var page = await RenderAsync(Bill);
        var vote = page.QuerySelector(".bill-vote")!;

        Assert.Equal(Root + $"records/{new string('b', 64)}.html", vote.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("House members: 262 Yes, 159 No, 12 not voting", vote.QuerySelector(".bill-tally-bar")!.GetAttribute("aria-label"));
        Assert.NotNull(vote.QuerySelector(".bill-badge"));
        var agreed = await RenderAsync(Bill with { Votes = [Bill.Votes[0] with { Alignment = PublishedVoteAlignment.PartiesAgree, Yea = null }] });
        Assert.Null(agreed.QuerySelector(".bill-badge"));
        Assert.Null(agreed.QuerySelector(".bill-tally"));
    }

    [Fact]
    public async Task ABillWithNoLinkedVotesOrIssuesShowsNoEmptySection()
    {
        var page = await RenderAsync(Bill with { Votes = [], Issues = [], PolicyArea = null });

        Assert.Null(page.QuerySelector(".bill-votes"));
        Assert.Null(page.QuerySelector(".bill-card"));
    }

    [Fact]
    public async Task ThePageHasNoCostOrDateSlotAndEscapesStoredText()
    {
        var page = await RenderAsync(Bill with { Summary = "<script>private()</script>" });

        Assert.Null(page.QuerySelector("script[src=''], .bill-main script"));
        Assert.DoesNotContain("how much it costs", page.Body!.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("<script>private()</script>", page.QuerySelector(".bill-summary p")!.TextContent);
    }

    [Fact]
    public async Task TheSourceFoldHoldsTheStatusTheCrsVersionTheRevisionAndTheWording()
    {
        var fold = (await RenderAsync(Bill)).QuerySelector("details#source")!;
        var text = fold.TextContent;

        Assert.Contains("Became law: Public Law 119-111. Action date 18 Sep 2026.", text);
        Assert.Contains("Status read 5 Nov 2026, 09:57 UTC. The bill can change after this time.", text);
        Assert.Contains("Version 55", text);
        Assert.Contains(Bill.Label!, text);
        Assert.Contains("2", fold.QuerySelector("dt:nth-of-type(3) + dd")!.TextContent);
    }

    private static async Task<IDocument> RenderAsync(BillPageData data)
    {
        await using var services = new ServiceCollection().BuildServiceProvider();
        await using var renderer = new HtmlRenderer(services, NullLoggerFactory.Instance);
        var html = await renderer.Dispatcher.InvokeAsync(async () =>
        {
            var output = await renderer.RenderComponentAsync<BillDetailPage>(ParameterView.FromDictionary(
                new Dictionary<string, object?> { [nameof(BillDetailPage.Data)] = data, [nameof(BillDetailPage.RootPath)] = Root }));
            return output.ToHtmlString();
        });
        return await new HtmlParser().ParseDocumentAsync(html);
    }
}
