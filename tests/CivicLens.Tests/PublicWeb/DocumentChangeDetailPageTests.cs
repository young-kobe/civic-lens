using AngleSharp.Dom;
using CivicLens.PublicWeb.Components.Evidence;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using static CivicLens.Tests.PublicWeb.DetailPageFixture;

namespace CivicLens.Tests.PublicWeb;

public sealed class DocumentChangeDetailPageTests
{
    [Fact]
    public async Task TheSummaryExplainsACorrectionTermBeforeTheLaterCorrectionHistory()
    {
        var name = Glossary.Term("correction").Name;
        var data = Change() with { Summary = name, Corrections = PublishedCorrectionListTests.History };

        var page = await RenderAsync<DocumentChangeDetailPage, DocumentChangePageData>(data);

        Assert.Equal(name, page.QuerySelector(".ai-summary button.term")?.TextContent);
        Assert.Single(page.QuerySelectorAll("[id=term-correction]"));
        Assert.Empty(page.QuerySelectorAll(".corrections button.term"));
    }

    [Theory]
    [InlineData(PublishedTextOrigin.Model)]
    [InlineData(PublishedTextOrigin.Person)]
    public async Task TheHeadlineCanExplainACorrectionBeforeTheSummaryAndTheCorrectionHistory(PublishedTextOrigin origin)
    {
        const string headline = "Correction (by us) to the visitor policy record";
        const string summary = "Correction (by us) to the account of the change.";
        var data = Change(origin) with { Headline = headline, Summary = summary, Corrections = PublishedCorrectionListTests.History };
        var page = await RenderAsync<DocumentChangeDetailPage, DocumentChangePageData>(data);

        Assert.Equal(headline, page.QuerySelector("h1")!.ShownText());
        Assert.Equal(summary, page.QuerySelector(".ai-summary p")!.ShownText());
        Assert.Null(page.QuerySelector(".ai-summary button[popovertarget=term-correction]"));
        Assert.Single(page.QuerySelectorAll("#term-correction"));
        var firstUse = origin == PublishedTextOrigin.Model ? "h1" : ".corrections";
        Assert.NotNull(page.QuerySelector($"{firstUse} button[popovertarget=term-correction]"));
    }

    [Fact]
    public void TheRedlineStrikesTheOldWordsAddsTheNewWordsAndKeepsTheWordsBetweenUnchanged()
    {
        var segments = DocumentChangeRedline.Build(SevenToThree(), BeforeText, AfterText);

        Assert.Equal([(RedlineKind.Context, "Visitors must book a tour at least "), (RedlineKind.Removed, "seven"), (RedlineKind.Added, "three"),
            (RedlineKind.Context, " days before the visit and bring a photo ID.")],
            segments.Select(segment => (segment.Kind, segment.Text)));
        Assert.Equal(BeforeText, string.Concat(segments.Where(segment => segment.Kind != RedlineKind.Added).Select(segment => segment.Text)));
        Assert.Equal(AfterText, string.Concat(segments.Where(segment => segment.Kind != RedlineKind.Removed).Select(segment => segment.Text)));
    }

    [Fact]
    public async Task ThePageShowsTheRedlineOfEachChangedPassageWithAVisibleLabelBeforeTheRemovedAndTheAddedText()
    {
        var page = await RenderAsync<DocumentChangeDetailPage, DocumentChangePageData>(Change());

        var removed = Assert.Single(page.QuerySelectorAll(".redline del"));
        var added = Assert.Single(page.QuerySelectorAll(".redline ins"));
        Assert.Equal(("seven", "Removed"), (removed.TextContent, removed.PreviousElementSibling!.TextContent));
        Assert.Equal(("three", "Added"), (added.TextContent, added.PreviousElementSibling!.TextContent));
        Assert.Equal(["Removed", "Added"], page.QuerySelectorAll(".redline .redline-label").Select(label => label.TextContent));
        Assert.Equal("Passage 1 of 1", page.QuerySelector(".detail-passage h3")!.TextContent);
    }

    [Fact]
    public async Task OnlyTheFirstTenPassagesShowAndTheNoteSaysHowManyChanged()
    {
        var page = await RenderAsync<DocumentChangeDetailPage, DocumentChangePageData>(Change(passages: 12));

        Assert.Equal(10, page.QuerySelectorAll(".detail-passage").Length);
        Assert.Contains("12 passages changed. The first 10 are shown.", page.QuerySelector("#change-what-title")!.ParentElement!.TextContent);
        Assert.StartsWith("Civic Lens saved this page twice. The text changed in 12 places", page.QuerySelector(".detail-lede")!.TextContent);
    }

    [Fact]
    public async Task WithoutAStatedChangeDateTheTimeFrameIsTheTwoExactChecksAndItSaysTheDayIsUnknown()
    {
        var page = await RenderAsync<DocumentChangeDetailPage, DocumentChangePageData>(Change());

        var text = page.QuerySelector("#change-when-title")!.ParentElement!.TextContent;
        Assert.Contains("between our check on 12 Oct 2026, 08:00 UTC and our check on 19 Oct 2026, 08:00 UTC", text);
        Assert.Contains("We do not know the day it changed.", text);
    }

    [Fact]
    public async Task AStatedChangeDateShowsWithItsEvidenceMarkedInTheLaterTextOfTheFold()
    {
        var page = await RenderAsync<DocumentChangeDetailPage, DocumentChangePageData>(Change(changeDate: new(2026, 10, 15)));

        Assert.Contains("The source gives the change date as 15 Oct 2026.", page.QuerySelector("#change-when-title")!.ParentElement!.TextContent);
        Assert.DoesNotContain("We do not know", page.QuerySelector("#change-when-title")!.ParentElement!.TextContent);
        Assert.NotNull(page.QuerySelector("#later-version ~ .doc-text mark[id=cite-change-date]"));
        Assert.Contains("Evidence for the actual change date, later version", page.QuerySelector("#source")!.TextContent);
    }

    [Fact]
    public async Task AModelSummaryCarriesItsLabelAndAPersonSummaryDoesNotAndBothKeepTheCaveatInTheCheckLine()
    {
        var model = await RenderAsync<DocumentChangeDetailPage, DocumentChangePageData>(Change());
        var person = await RenderAsync<DocumentChangeDetailPage, DocumentChangePageData>(Change(PublishedTextOrigin.Person));

        Assert.Equal(ChangeLabel, model.QuerySelector(".ai-label")!.TextContent);
        Assert.Contains("A model wrote only this summary.", model.QuerySelector(".ai-summary-check")!.TextContent);
        Assert.Null(person.QuerySelector(".ai-summary-ai"));
        Assert.DoesNotContain("model", person.QuerySelector(".ai-summary-check")!.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.All([model, person], page => Assert.Contains(DocumentChangeWords.Limit, page.QuerySelector(".ai-summary-check")!.TextContent));
        Assert.All([model, person], page => Assert.Null(page.QuerySelector(".caveat")));
    }

    [Fact]
    public async Task ModelProseAcrossTheSummarySignificanceAndLimitsSharesOneFirstUsePopover()
    {
        var data = Change() with { Summary = "Bill", Significance = "BILL", Limits = "bill" };

        var page = await RenderAsync<DocumentChangeDetailPage, DocumentChangePageData>(data);

        var prose = page.QuerySelectorAll(".ai-summary p:not(.ai-summary-check)");
        Assert.Equal(3, prose.Length);
        Assert.Equal(["Bill", "BILL", "bill"], prose.Select(VisibleText));
        Assert.Single(page.QuerySelectorAll(".ai-summary button.term"));
        Assert.Equal("term-bill", page.QuerySelector(".ai-summary [popover]")!.Id);
    }

    [Fact]
    public async Task ThePageShowsTheLabelOfTheViewAndNoLabelWhenTheViewSetsNone()
    {
        var page = await RenderAsync<DocumentChangeDetailPage, DocumentChangePageData>(Change(label: null));

        Assert.Null(page.QuerySelector(".ai-label"));
        Assert.Null(page.QuerySelector(".ai-summary-ai"));
    }

    private static string VisibleText(IElement element) => string.Concat(element.ChildNodes
        .Where(node => node is not IElement child || !child.HasAttribute("popover"))
        .Select(node => node.TextContent));

    [Fact]
    public async Task ACitationLinkNamesItsNumberOnlyWhenThereIsMoreThanOneCitation()
    {
        var one = await RenderAsync<DocumentChangeDetailPage, DocumentChangePageData>(Change(citations: 1));
        var two = await RenderAsync<DocumentChangeDetailPage, DocumentChangePageData>(Change());

        Assert.Equal(": later version, quote", one.QuerySelector("#source .choices a .visually-hidden")!.TextContent);
        Assert.Equal(": later version, quote 1", two.QuerySelector("#source .choices a .visually-hidden")!.TextContent);
    }

    [Fact]
    public async Task TheSummaryBoxHoldsTheSignificanceAndTheLimitsWithTheirApprovedHeadings()
    {
        var page = await RenderAsync<DocumentChangeDetailPage, DocumentChangePageData>(Change());

        var box = page.QuerySelector(".ai-summary-ai")!;
        Assert.Equal(["Why it matters", "What the evidence cannot establish"], box.QuerySelectorAll("h3").Select(heading => heading.TextContent));
        Assert.Contains("The text alone does not show the reason.", box.TextContent);
    }

    [Fact]
    public async Task BothFullVersionsAreInTheFoldWithTheirCitationsMarkedAndTheTrailEndsWithTheTitle()
    {
        var page = await RenderAsync<DocumentChangeDetailPage, DocumentChangePageData>(Change());

        Assert.Equal(["Earlier version text", "Later version text"], page.QuerySelectorAll("#source .doc-text").Select(text => text.GetAttribute("aria-label")));
        Assert.Equal(BeforeText, page.QuerySelectorAll("#source .doc-text")[0].TextContent);
        Assert.Equal(["tour", "three"], page.QuerySelectorAll("#source .doc-text mark").Select(mark => mark.TextContent));
        Assert.Equal(["#cite-1", "#cite-2"], page.QuerySelectorAll("#source .choices a").Select(link => link.GetAttribute("href")!).ToArray());
        Assert.Equal("Example Agency changed its visitor policy page", page.QuerySelector(".public-breadcrumbs [aria-current=page]")!.TextContent);
        Assert.NotNull(Assert.Single(page.QuerySelectorAll("a[href*='report']")).Closest(".public-footer"));
    }

    [Fact]
    public async Task TheCorrectionBannerFollowsTheHeadlineOnlyForAPersonCorrectionAndARoutineUpdateShowsTheMutedLineInTheHeader()
    {
        var corrected = await RenderAsync<DocumentChangeDetailPage, DocumentChangePageData>(Change() with { RevisionNumber = 4, Corrections = PublishedCorrectionListTests.History });
        var plain = await RenderAsync<DocumentChangeDetailPage, DocumentChangePageData>(Change());
        var updated = await RenderAsync<DocumentChangeDetailPage, DocumentChangePageData>(Change() with { Corrections = [PublishedUpdateLineTests.SourceUpdate] });

        PublishedCorrectionBannerTests.AssertTheBannerFollowsTheHeadline(corrected);
        PublishedCorrectionListTests.AssertFullHistory(corrected);
        PublishedCorrectionListTests.AssertEmptyHistory(plain);
        Assert.Null(plain.QuerySelector(".correction-banner"));
        Assert.Null(corrected.QuerySelector(".update-line"));
        Assert.Null(plain.QuerySelector(".update-line"));
        PublishedUpdateLineTests.AssertTheLineIsInTheRecordHeader(updated);
        Assert.Single(updated.QuerySelectorAll(".corrections > li"));
    }

    [Fact]
    public async Task TheDocumentChangeSummaryLabelIsDrawnByAiLabel()
    {
        AiLabelAssert.DrawnByAiLabel(await RenderAsync<DocumentChangeDetailPage, DocumentChangePageData>(Change()), ChangeLabel);
    }
}
