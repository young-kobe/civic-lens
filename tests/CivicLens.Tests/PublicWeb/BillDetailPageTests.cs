using System.Text.RegularExpressions;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.PublicWeb.Components.Ui;
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

    private static readonly BillPageData Bill = new(7, RecordId, 2, 119, "H.R. 5334", "The Example Act of 2026", "H.R. 5334 headline", PublishedChamber.House,
        "The bill would add a tax credit.", PublishedTextOrigin.Model, "AI summary. Drafted by an AI model from reviewed records.", new(2025, 9, 11))
    {
        Sponsors = [Sponsor],
        Cosponsors = new(PublishedChamber.House,
            [new("p2", "Ann Two", "Democrat", new(2025, 9, 11)), new("p3", "Bob Three", "Republican", new(2025, 9, 11))],
            [new("p4", "Cy Four", "Democrat", new(2025, 10, 2)), new("p5", "Di Five", null, new(2025, 10, 3))]),
        Steps = [new("Introduced in the House", "11 Sep 2025, by Pat One"), new("Became law", "18 Sep 2026, Public Law 119-111")],
        PublicLaw = "119-111",
        BecameLawOn = new(2026, 9, 18),
        Status = "Became law: Public Law 119-111.",
        StatusLabel = "Became law",
        CongressGovUrl = "https://www.congress.gov/bill/119th-congress/house-bill/5334",
        StatusDate = new(2026, 9, 18),
        StatusReadAtUtc = new(2026, 11, 5, 9, 57, 0, TimeSpan.Zero),
        CrsText = "This bill imposes sanctions.\nIt extends a law.",
        CrsVersion = "55",
        Issues = [new("foreign-policy", "Foreign policy and war")],
        PolicyArea = "International Affairs",
        BillLine = "This bill imposes sanctions.",
        Votes = [new(new string('b', 64), new(2026, 9, 16), 119, 2, 410, "House agrees to Senate changes", "AI summary. Drafted by an AI model from reviewed records.", "Vote to pass or agree to", "Passed", [])]
    };

    [Fact]
    public async Task TheHeadingNamesTheBillAndItsOfficialTitleAndTheTrailEndsWithTheBillNumber()
    {
        var page = await RenderAsync(Bill);

        Assert.Equal("H.R. 5334, The Example Act of 2026", page.QuerySelector("main h1")!.TextContent);
        var trail = page.QuerySelectorAll(".public-breadcrumbs li").Select(item => (item.QuerySelector("a") ?? item).TextContent.Trim()).ToArray();
        Assert.Equal(["Home", "Bills", "H.R. 5334"], trail);
        Assert.Equal("Bill in the 119th Congress", page.QuerySelector(".bill-eyebrow")!.TextContent);
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
        var summary = page.QuerySelector(".ai-summary")!;
        var fold = page.QuerySelector(".bill-main > details.source-fold")!;

        Assert.Same(fold, summary.NextElementSibling);
        Assert.True(summary.ClassList.Contains("ai-summary-ai"));
        Assert.Equal(Bill.Label, summary.QuerySelector(".ai-summary-label")!.TextContent);
        Assert.Equal(["This bill imposes sanctions.", "It extends a law."], fold.QuerySelectorAll("p").Select(paragraph => paragraph.TextContent));
        Assert.Null((await RenderAsync(Bill with { CrsText = null })).QuerySelector(".bill-main > details.source-fold"));
    }

    [Fact]
    public async Task OnlyAModelSummaryWithALabelGetsTheAiStyleAndNoLabelColumnShowsPlainText()
    {
        var code = (await RenderAsync(Bill with { TextOrigin = PublishedTextOrigin.Code, Label = PublishedRecordLabels.CodeWording })).QuerySelector(".ai-summary")!;
        var plain = (await RenderAsync(Bill with { Label = null })).QuerySelector(".ai-summary")!;

        Assert.False(code.ClassList.Contains("ai-summary-ai"));
        Assert.Equal(PublishedRecordLabels.CodeWording, code.QuerySelector(".ai-summary-label")!.TextContent);
        Assert.False(plain.ClassList.Contains("ai-summary-ai"));
        Assert.Null(plain.QuerySelector(".ai-summary-label"));
    }

    [Fact]
    public async Task TheCosponsorSplitIsOneSharedPartyBarThatNamesTheChamberTheBaseAndEachPartyCount()
    {
        var page = await RenderAsync(Bill);
        var section = page.QuerySelector("#bill-backers-title")!.ParentElement!;
        var bar = section.QuerySelector(".party-bar")!;

        Assert.Contains("This site tracks 4 House members who cosponsored it: 2 on the day it was introduced and 2 who joined later.", section.QuerySelector(".bill-lede")!.TextContent);
        Assert.Equal("Tracked House cosponsors by party on the day each one joined", bar.QuerySelector("figcaption")!.TextContent);
        Assert.Equal(["2 Democrats", "1 Republicans", "1 No party listed"], bar.QuerySelectorAll(".party-bar-legend li").Select(item => Regex.Replace(item.TextContent, @"\s+", " ").Trim()));
        Assert.Equal(["solid", "stripes", "open"], bar.QuerySelectorAll(".party-bar-legend rect").Select(rect => rect.GetAttribute("data-fill")));
        Assert.Null(section.QuerySelector(".bill-party"));
        Assert.Contains("Cy Four (2 Oct 2025)", section.QuerySelector("dd:last-of-type")!.TextContent);
    }

    [Fact]
    public async Task ABillWithNoTrackedCosponsorShowsNoPartyBar()
    {
        var page = await RenderAsync(Bill with { Cosponsors = new(PublishedChamber.House, [], []) });

        Assert.Null(page.QuerySelector(".party-bar"));
    }

    [Fact]
    public async Task LinkedVotesAreSharedSingleMemberRowsWithTheirAiLabelAndNoCastBecauseThePageNamesNoMember()
    {
        var page = await RenderAsync(Bill);
        var row = page.QuerySelector(".bill-votes .shared-vote-rows.single .shared-vote-row")!;

        Assert.Equal(Root + $"records/{new string('b', 64)}.html", row.QuerySelector("h3 a")!.GetAttribute("href"));
        Assert.Contains("roll call 410, 119th Congress, session 2. Vote to pass or agree to. Result: Passed", row.QuerySelector(".shared-vote-meta")!.TextContent);
        Assert.Equal("AI summary. Drafted by an AI model from reviewed records.", row.QuerySelector(".shared-vote-label")!.TextContent);
        Assert.Null(row.QuerySelector(".shared-vote-casts"));
        Assert.Null(page.QuerySelector(".bill-tally, .bill-badge, .shared-vote-rows > h2"));
    }

    [Fact]
    public async Task ALinkedVoteShowsTheChamberTotalsAsTheSharedPartyBarAndTheSplitBadgeFromItsAlignment()
    {
        var totals = VotePageView.CastBar("House members", PartyTone.Neutral, 262, 159, 0, 12, 0, false);
        var votes = Bill.Votes.Select(vote => vote with { Totals = totals, Alignment = PublishedVoteAlignment.Split }).ToArray();

        var page = await RenderAsync(Bill with { Votes = votes });
        var row = page.QuerySelector(".bill-votes .shared-vote-row")!;

        var legend = row.QuerySelectorAll(".party-bar-legend li").Select(item => Regex.Replace(item.TextContent, @"\s+", " ").Trim());
        Assert.Equal(["262 Yes", "159 No", "12 Not voting"], legend);
        Assert.Equal("Split vote", row.QuerySelector(".badge")!.TextContent);
        Assert.Null(row.QuerySelector(".party-bar-mark"));
    }

    [Fact]
    public async Task ALinkedVoteWithNoTotalsAndNoAlignmentShowsNoBarAndNoBadge()
    {
        var row = (await RenderAsync(Bill)).QuerySelector(".bill-votes .shared-vote-row")!;

        Assert.Null(row.QuerySelector(".party-bar, .badge"));
    }

    [Fact]
    public async Task TheSenateGapNoteShowsOnlyWhenTheStepperHasASenateStepAndSaysNothingAboutWhy()
    {
        const string note = "This site has no Senate roll call vote on this bill.";
        var senateStep = Bill with { Steps = [.. Bill.Steps, new("Passed the Senate", "7 Aug 2026") { Chamber = PublishedChamber.Senate }], SenateVoteGap = true };

        var shown = await RenderAsync(senateStep);
        var hidden = await RenderAsync(Bill);

        Assert.Contains(note, shown.QuerySelector(".bill-votes")!.TextContent);
        Assert.DoesNotContain("release", shown.QuerySelector(".bill-votes p.muted")!.TextContent);
        Assert.DoesNotContain(note, hidden.Body!.TextContent);
        Assert.Contains(note, (await RenderAsync(senateStep with { Votes = [] })).QuerySelector(".bill-votes")!.TextContent);
    }

    [Fact]
    public async Task TheMembersCardHoldsTheApprovedHeadingAndPromptAndTheRecordIdForTheDeviceFill()
    {
        var card = (await RenderAsync(Bill)).QuerySelector(".bill-members")!;

        Assert.Equal("Your members and this bill", card.QuerySelector("h2")!.TextContent);
        Assert.Equal(RecordId, card.GetAttribute("data-record-id"));
        Assert.Equal("Enter your ZIP to see how your members relate to this bill. Find my members", card.QuerySelector("[data-bill-members-prompt]")!.TextContent);
        Assert.Equal(Root + "index.html#zip-results", card.QuerySelector("[data-bill-members-prompt] a")!.GetAttribute("href"));
        Assert.Equal(0, card.QuerySelector("[data-bill-members-body]")!.ChildElementCount);
    }

    [Fact]
    public async Task TheStatusBadgeIsTheSharedLabelAndAnActionThatNoRuleMatchesGetsNoBadge()
    {
        var page = await RenderAsync(Bill);
        var none = await RenderAsync(Bill with { StatusLabel = null });

        Assert.Equal("Became law", page.QuerySelector(".bill-head .badge")!.TextContent);
        Assert.Null(none.QuerySelector(".badge"));
        Assert.Contains("Became law: Public Law 119-111.", none.QuerySelector("details#source")!.TextContent);
    }

    [Fact]
    public async Task TheBillLineShowsUnderTheOfficialTitleWithTheSharedNoteOnceAndNoLineLeavesOnlyTheTitle()
    {
        var page = await RenderAsync(Bill);
        var none = await RenderAsync(Bill with { BillLine = null });

        Assert.Equal("H.R. 5334, The Example Act of 2026", page.QuerySelector("main h1")!.TextContent);
        Assert.Equal("This bill imposes sanctions.", page.QuerySelector(".bill-head .bill-line")!.TextContent);
        Assert.Single(page.QuerySelectorAll("main p"), note => note.TextContent == PublishedWording.BillLineSource);
        Assert.Null(none.QuerySelector(".bill-line"));
        Assert.DoesNotContain(PublishedWording.BillLineSource, none.Body!.TextContent);
        Assert.Equal("H.R. 5334, The Example Act of 2026", none.QuerySelector("main h1")!.TextContent);
    }

    [Fact]
    public async Task TheSourceFoldLinksTheCongressGovPageOfTheBill()
    {
        var link = (await RenderAsync(Bill)).QuerySelector("details#source a")!;

        Assert.Equal("Bill on Congress.gov", link.TextContent);
        Assert.Equal(Bill.CongressGovUrl, link.GetAttribute("href"));
    }

    [Fact]
    public async Task ABillWithNoLinkedVotesOrIssuesShowsNoEmptySection()
    {
        var page = await RenderAsync(Bill with { Votes = [], Issues = [], PolicyArea = null });

        Assert.Null(page.QuerySelector(".bill-votes"));
        Assert.Null(page.QuerySelector(".bill-card:not(.bill-members)"));
    }

    [Fact]
    public async Task ThePageHasNoCostOrDateSlotAndEscapesStoredText()
    {
        var page = await RenderAsync(Bill with { Summary = "<script>private()</script>" });

        Assert.Null(page.QuerySelector("script[src=''], .bill-main script"));
        Assert.DoesNotContain("how much it costs", page.Body!.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Equal("<script>private()</script>", page.QuerySelector(".ai-summary p")!.TextContent);
    }

    [Fact]
    public async Task TheSourceFoldHoldsTheStatusTheCrsVersionAndTheRevision()
    {
        var fold = (await RenderAsync(Bill)).QuerySelector("details#source")!;
        var text = fold.TextContent;

        Assert.Contains("Became law: Public Law 119-111. Action date 18 Sep 2026.", text);
        Assert.Contains("Status read 5 Nov 2026, 09:57 UTC. The bill can change after this time.", text);
        Assert.Contains("Version 55", text);
        Assert.Equal("2", fold.QuerySelectorAll("dt").Single(term => term.TextContent == "Revision").NextElementSibling!.TextContent);
    }

    [Fact]
    public async Task TheStatusReadTimeShowsOnceAndOnlyInTheSourceFold()
    {
        const string statusRead = "Status read 5 Nov 2026, 09:57 UTC. The bill can change after this time.";
        var page = await RenderAsync(Bill);

        Assert.Single(page.QuerySelectorAll("dd, p"), element => element.TextContent.Contains(statusRead, StringComparison.Ordinal));
        Assert.Contains(statusRead, page.QuerySelector("details#source")!.TextContent);
    }

    [Fact]
    public async Task TheCheckedLineShowsTheApprovalAndFirstPublicationTimesOnlyWhenBothAreKnown()
    {
        var times = Bill with { CheckedAtUtc = new(2026, 11, 5, 10, 5, 0, TimeSpan.Zero), FirstPublishedAtUtc = new(2026, 11, 5, 10, 7, 0, TimeSpan.Zero) };

        var shown = (await RenderAsync(times)).QuerySelector("details#source")!;
        var hidden = (await RenderAsync(times with { CheckedAtUtc = null })).QuerySelector("details#source")!;

        Assert.Equal("5 Nov 2026, 10:05 UTC. First published 5 Nov 2026, 10:07 UTC.",
            shown.QuerySelectorAll("dt").Single(term => term.TextContent == "Checked").NextElementSibling!.TextContent);
        Assert.DoesNotContain(hidden.QuerySelectorAll("dt"), term => term.TextContent == "Checked");
    }

    [Fact]
    public async Task TheCrsLineShowsOnlyWhenTheStoredOriginSaysTheModelDraftedFromTheCrsSummary()
    {
        const string line = "The text above summarizes the Congressional Research Service summary.";

        Assert.Contains(line, (await RenderAsync(Bill with { SummaryFromCrs = true })).QuerySelector(".ai-summary")!.TextContent);
        Assert.DoesNotContain(line, (await RenderAsync(Bill with { SummaryFromCrs = false })).Body!.TextContent);
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
