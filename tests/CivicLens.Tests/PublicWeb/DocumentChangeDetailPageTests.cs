using CivicLens.PublicWeb.Components.Evidence;
using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using static CivicLens.Tests.PublicWeb.DetailPageFixture;

namespace CivicLens.Tests.PublicWeb;

public sealed class DocumentChangeDetailPageTests
{
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
    public async Task ThePageShowsTheRedlineOfEachChangedPassageWithHiddenLabelsForRemovedAndAddedText()
    {
        var page = await RenderAsync<DocumentChangeDetailPage, DocumentChangePageData>(Change());

        Assert.Equal("seven", Assert.Single(page.QuerySelectorAll(".redline del")).TextContent.Replace("Removed: ", ""));
        Assert.Equal("three", Assert.Single(page.QuerySelectorAll(".redline ins")).TextContent.Replace("Added: ", ""));
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

        Assert.Equal(ChangeLabel, model.QuerySelector(".ai-summary-label")!.TextContent);
        Assert.Contains("A model wrote only this summary.", model.QuerySelector(".ai-summary-check")!.TextContent);
        Assert.Null(person.QuerySelector(".ai-summary-ai"));
        Assert.DoesNotContain("model", person.QuerySelector(".ai-summary-check")!.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.All([model, person], page => Assert.Contains(DocumentChangeWords.Limit, page.QuerySelector(".ai-summary-check")!.TextContent));
        Assert.All([model, person], page => Assert.Null(page.QuerySelector(".caveat")));
    }

    [Fact]
    public async Task ThePageShowsTheLabelOfTheViewAndNoLabelWhenTheViewSetsNone()
    {
        var page = await RenderAsync<DocumentChangeDetailPage, DocumentChangePageData>(Change(label: null));

        Assert.Null(page.QuerySelector(".ai-summary-label"));
        Assert.Null(page.QuerySelector(".ai-summary-ai"));
    }

    [Fact]
    public async Task AnIssueThatTheReleaseDoesNotHoldIsPlainTextInTheFold()
    {
        var data = Change() with { Issues = [new("budget", "Federal budget and debt", true), new("unlisted", "unlisted", false)] };
        var page = await RenderAsync<DocumentChangeDetailPage, DocumentChangePageData>(data);

        var issues = page.QuerySelectorAll("#source dd").Single(value => value.TextContent.Contains("unlisted"));
        Assert.Equal(["Federal budget and debt"], issues.QuerySelectorAll("a").Select(link => link.TextContent));
    }

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
}
