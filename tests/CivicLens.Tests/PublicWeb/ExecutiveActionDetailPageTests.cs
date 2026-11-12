using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using static CivicLens.Tests.PublicWeb.DetailPageFixture;

namespace CivicLens.Tests.PublicWeb;

public sealed class ExecutiveActionDetailPageTests
{
    [Fact]
    public async Task TheLedeKeepsTheExactSignedAndPrintedDatesAndTheCitation()
    {
        var page = await RenderAsync<ExecutiveActionDetailPage, ExecutiveActionPageData>(Action());

        Assert.Equal("Signed by President Donald Trump on 16 Oct 2026. Printed in the Federal Register on 21 Oct 2026, citation 91 FR 66627.",
            page.QuerySelector(".detail-lede")!.TextContent);
        Assert.Equal(["Executive order", "Federal Register document 2026-21599"], page.QuerySelectorAll(".detail-badges .badge").Select(badge => badge.TextContent));
        Assert.Equal(["16 Oct 2026", "21 Oct 2026", "91 FR 66627"], page.QuerySelectorAll(".detail-facts dd").Take(3).Select(value => value.TextContent.Trim()));
        Assert.Equal(["Signed", "Published in the Federal Register", "Federal Register citation"], page.QuerySelectorAll(".detail-facts dt").Take(3).Select(name => name.TextContent.Trim()));
    }

    [Fact]
    public async Task TheCaveatLivesInTheCheckLineAndNoModelTextOrLabelAppears()
    {
        var page = await RenderAsync<ExecutiveActionDetailPage, ExecutiveActionPageData>(Action());

        Assert.Contains(ExecutiveActionWords.Caveat, page.QuerySelector(".ai-summary-check")!.TextContent);
        Assert.Null(page.QuerySelector(".ai-summary-ai"));
        Assert.Null(page.QuerySelector(".caveat"));
        Assert.Contains("Civic Lens did not summarize this document", page.QuerySelector(".ai-summary-check")!.TextContent);
    }

    [Fact]
    public async Task TheSummarySlotShowsTheViewLabelTheSentencesAndTheCheckLineAndNoCutNoteWhenTheModelReadAllOfTheText()
    {
        var page = await RenderAsync<ExecutiveActionDetailPage, ExecutiveActionPageData>(WithSummary(cut: false));

        var slot = page.QuerySelector(".ai-summary-ai")!;
        Assert.Equal(ExecutiveSummaryWords.Label, slot.QuerySelector(".ai-summary-label")!.TextContent);
        Assert.Equal(["The order tells agencies to act.", "Each agency reports in 30 days."], slot.QuerySelectorAll("p").Where(p => p.ClassName is null or "").Select(p => p.TextContent));
        Assert.StartsWith(ExecutiveActionWords.SummaryCheck, slot.QuerySelector(".ai-summary-check")!.TextContent);
        Assert.DoesNotContain("Civic Lens did not summarize", page.TextContent);
        Assert.DoesNotContain(ExecutiveSummaryWords.CutNote, page.TextContent);
    }

    [Fact]
    public async Task ACutSummaryShowsTheCutNoteInsideTheSlot()
    {
        var page = await RenderAsync<ExecutiveActionDetailPage, ExecutiveActionPageData>(WithSummary(cut: true));

        Assert.Contains(ExecutiveSummaryWords.CutNote, page.QuerySelector(".ai-summary-ai")!.TextContent);
    }

    [Fact]
    public async Task ASummaryWithNoLabelGetsNoModelStylingBecauseOnlyTheViewLabelMakesTextModelText()
    {
        var page = await RenderAsync<ExecutiveActionDetailPage, ExecutiveActionPageData>(
            Action() with { Summary = new(PublishedTextOrigin.Model, null, false, ["A sentence."]) });

        Assert.Null(page.QuerySelector(".ai-summary-ai"));
        Assert.Null(page.QuerySelector(".ai-summary-label"));
    }

    private static ExecutiveActionPageData WithSummary(bool cut) => Action() with
    {
        Summary = new(PublishedTextOrigin.Model, ExecutiveSummaryWords.Label, cut, ["The order tells agencies to act.", "Each agency reports in 30 days."])
    };

    [Fact]
    public async Task EachCorrectionNoticeShowsItsExactDateAndLinksToTheNotice()
    {
        var page = await RenderAsync<ExecutiveActionDetailPage, ExecutiveActionPageData>(
            Action(new ExecutiveActionNoticeData("2026-22000", new(2026, 11, 2), "https://www.federalregister.gov/d/2026-22000")));

        var notice = Assert.Single(page.QuerySelectorAll(".detail-facts dd a"), link => link.TextContent == "Open the correction");
        Assert.Equal("https://www.federalregister.gov/d/2026-22000", notice.GetAttribute("href"));
        Assert.Contains("published a correction on 2 Nov 2026", notice.ParentElement!.TextContent);
    }

    [Fact]
    public async Task ThePresidentLinksToTheOfficialPageAndTheSourceFoldNamesTheOfficialDocument()
    {
        var page = await RenderAsync<ExecutiveActionDetailPage, ExecutiveActionPageData>(Action());

        Assert.Equal(Root + "officials/president.html", page.QuerySelectorAll(".detail-facts dd a").Single(link => link.TextContent == "Donald Trump").GetAttribute("href"));
        Assert.Equal("https://www.federalregister.gov/d/2026-21599", page.QuerySelector(".detail-facts a.btn")!.GetAttribute("href"));
        var fold = page.QuerySelector("#source")!.TextContent;
        Assert.Contains("Federal Register document 2026-21599", fold);
    }

    [Fact]
    public async Task DocumentTargetsKeepTheirOrderExactReferencesAndOnlyLinkTargetsInTheRelease()
    {
        var page = await RenderAsync<ExecutiveActionDetailPage, ExecutiveActionPageData>(Action() with
        {
            Targets =
            [
                new(PublishedExecutiveActionRelation.Amends, PublishedExecutiveActionKind.ExecutiveOrder, "14100", "EO 14100, January 3, 2025", new('a', 64)),
                new(PublishedExecutiveActionRelation.Revokes, PublishedExecutiveActionKind.Proclamation, "10001", "Proclamation 10001: café & <notice>", null)
            ]
        });

        var section = page.QuerySelector("#executive-links-title")!.ParentElement!;
        Assert.Equal("Links to other documents", section.QuerySelector("h2")!.TextContent);
        var rows = section.QuerySelectorAll("li");
        Assert.Equal(2, rows.Length);
        Assert.Contains("Amends: Executive order 14100", rows[0].TextContent);
        Assert.Equal("EO 14100, January 3, 2025", rows[0].QuerySelector(".detail-target-reference")!.TextContent);
        Assert.Equal(Root + "records/" + new string('a', 64) + ".html", rows[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains("Revokes: Proclamation 10001", rows[1].TextContent);
        Assert.Equal("Proclamation 10001: café & <notice>", rows[1].QuerySelector(".detail-target-reference")!.TextContent);
        Assert.Null(rows[1].QuerySelector("a"));
        Assert.Null(rows[1].QuerySelector("notice"));
    }

    [Fact]
    public async Task TheDocumentLinksSectionIsOmittedWhenThereAreNoTargets()
    {
        var page = await RenderAsync<ExecutiveActionDetailPage, ExecutiveActionPageData>(Action());

        Assert.Null(page.QuerySelector("#executive-links-title"));
    }

    [Fact]
    public async Task TheTrailEndsWithTheTitleBecauseTheKindHasNoShortNameAndTheFooterIsTheOnlyReportLink()
    {
        var page = await RenderAsync<ExecutiveActionDetailPage, ExecutiveActionPageData>(Action());

        Assert.Equal("Implementing the Sanctioning Russia and Iran Act", page.QuerySelector(".public-breadcrumbs [aria-current=page]")!.TextContent);
        Assert.Equal("Executive actions", page.QuerySelectorAll(".public-breadcrumbs a")[1].TextContent);
        var report = Assert.Single(page.QuerySelectorAll("a[href*='report']"));
        Assert.NotNull(report.Closest(".public-footer"));
        Assert.Contains("Report a problem with this executive action", report.TextContent);
    }
}
