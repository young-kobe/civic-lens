using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using static CivicLens.Tests.PublicWeb.DetailPageFixture;

namespace CivicLens.Tests.PublicWeb;

public sealed class BriefDetailPageTests
{
    private const string BriefLabel = "An AI draft from reviewed records.";
    private static readonly string BriefId = new('b', 64);
    private static readonly string StatementCited = new('5', 64);

    private static BriefPageData Brief(string? label = BriefLabel) => new(
        7, BriefId, 3, new DateOnly(2026, 11, 2), "The week in Congress", PublishedTextOrigin.Model, label,
        [new PublishedBriefParagraph { Text = "First <b>paragraph</b>.", CitationIds = ["R1", "R2"] }, new PublishedBriefParagraph { Text = "Second paragraph.", CitationIds = ["R2"] }],
        [new PublishedBriefCitation { Id = "R1", Kind = PublishedRecordKind.Statement, RecordId = StatementCited, RevisionNumber = 2, Headline = "Kelly announces a bill" },
         new PublishedBriefCitation { Id = "R2", Kind = PublishedRecordKind.ExecutiveAction, RecordId = ActionId, RevisionNumber = 1, Headline = "An order" }]);

    [Fact]
    public async Task TheHeadWeekAndEyebrowNameTheWeekFromTheStoredWeekStart()
    {
        var page = await RenderAsync<BriefDetailPage, BriefPageData>(Brief());

        Assert.Equal("The week in Congress", page.QuerySelector("h1")!.TextContent);
        Assert.Equal("Weekly brief | Week of 2 Nov 2026", page.QuerySelector(".detail-eyebrow")!.TextContent);
        Assert.Equal("Starts Monday 2 Nov 2026, New York time", page.QuerySelector(".detail-lede")!.TextContent);
        Assert.Equal("Week of 2 Nov 2026", page.QuerySelector(".public-breadcrumbs [aria-current=page]")!.TextContent);
    }

    [Fact]
    public async Task TheModelTextCarriesTheLabelOfTheViewAndTheCheckLineHoldsTheSampleCaveatInsteadOfAFootCaveat()
    {
        var page = await RenderAsync<BriefDetailPage, BriefPageData>(Brief());

        Assert.Equal(BriefLabel, page.QuerySelector(".ai-summary-label")!.TextContent);
        Assert.Contains(PublishedBriefView.SampleNote, page.QuerySelector(".ai-summary-check")!.TextContent);
        Assert.Equal("#cited", page.QuerySelector(".ai-summary-check a")!.GetAttribute("href"));
        Assert.Null(page.QuerySelector(".caveat"));
        Assert.Equal(1, page.QuerySelectorAll(".ai-summary-label").Length);
    }

    [Fact]
    public async Task TheModelTextGetsTheAiBoxAndAPersonWrittenBriefDoesNot()
    {
        var model = await RenderAsync<BriefDetailPage, BriefPageData>(Brief());
        var person = await RenderAsync<BriefDetailPage, BriefPageData>(Brief() with { TextOrigin = PublishedTextOrigin.Person });

        Assert.NotNull(model.QuerySelector(".ai-summary-ai"));
        Assert.Null(person.QuerySelector(".ai-summary-ai"));
    }

    [Fact]
    public async Task AnUnlabeledBriefShowsPlainTextAndInventsNoAiLabel()
    {
        var page = await RenderAsync<BriefDetailPage, BriefPageData>(Brief(label: null));

        Assert.Null(page.QuerySelector(".ai-summary-label"));
        Assert.Null(page.QuerySelector(".ai-summary-ai"));
        Assert.Contains("First <b>paragraph</b>.", page.QuerySelector(".ai-summary")!.TextContent);
    }

    [Fact]
    public async Task EachParagraphKeepsItsCitationLinksInOrderAndEachLinkFindsItsCitedRecord()
    {
        var page = await RenderAsync<BriefDetailPage, BriefPageData>(Brief());

        var paragraphs = page.QuerySelectorAll(".ai-summary p.record-body");
        Assert.Equal(["#citation-R1", "#citation-R2"], paragraphs[0].QuerySelectorAll("a").Select(link => link.GetAttribute("href")));
        Assert.Equal(["[R1]", "[R2]"], paragraphs[0].QuerySelectorAll("a").Select(link => link.TextContent));
        Assert.Equal(["#citation-R2"], paragraphs[1].QuerySelectorAll("a").Select(link => link.GetAttribute("href")));
        Assert.All(new[] { "citation-R1", "citation-R2" }, id => Assert.NotNull(page.QuerySelector("#" + id)));
    }

    [Fact]
    public async Task EachCitedRecordKeepsItsIdHeadlineKindRevisionAndTheStaticRecordLink()
    {
        var page = await RenderAsync<BriefDetailPage, BriefPageData>(Brief());

        var rows = page.QuerySelectorAll(".detail-cited li");
        Assert.Equal(["R1", "R2"], rows.Select(row => row.QuerySelector(".choice-label")!.TextContent));
        Assert.Equal([Root + $"records/{StatementCited}.html", Root + $"records/{ActionId}.html"], rows.Select(row => row.QuerySelector("a")!.GetAttribute("href")));
        Assert.Equal(["Statement, revision 2", "Executive action, revision 1"], rows.Select(row => row.QuerySelector(".muted")!.TextContent));
        Assert.Equal("Cited records", page.QuerySelector("#brief-cited-title")!.TextContent);
    }
}
