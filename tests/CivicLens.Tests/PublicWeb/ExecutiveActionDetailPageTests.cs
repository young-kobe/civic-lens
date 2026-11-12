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
