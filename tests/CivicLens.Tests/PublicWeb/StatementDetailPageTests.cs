using CivicLens.PublicWeb.Publication;
using CivicLens.Publication.Contracts;
using static CivicLens.Tests.PublicWeb.DetailPageFixture;

namespace CivicLens.Tests.PublicWeb;

public sealed class StatementDetailPageTests
{
    [Fact]
    public async Task AModelSummaryCarriesItsLabelAndItsCheckLineHoldsTheCaveatsInsteadOfAFootCaveat()
    {
        var page = await RenderAsync<StatementDetailPage, StatementPageData>(Statement());

        var summary = page.QuerySelector(".detail-ai")!;
        Assert.Equal(StatementPageView.AiLabel, summary.QuerySelector(".detail-ai-label")!.TextContent);
        Assert.Contains("A model wrote only the summary above.", summary.QuerySelector(".detail-check")!.TextContent);
        Assert.Equal("#source", summary.QuerySelector(".detail-check a")!.GetAttribute("href"));
        Assert.Null(page.QuerySelector(".caveat"));
    }

    [Fact]
    public async Task APersonSummaryHasNoAiLabelAndNeverSaysAModelWroteIt()
    {
        var page = await RenderAsync<StatementDetailPage, StatementPageData>(Statement(PublishedTextOrigin.Person));

        Assert.Null(page.QuerySelector(".detail-ai"));
        Assert.Null(page.QuerySelector(".detail-ai-label"));
        Assert.DoesNotContain("model", page.QuerySelector(".detail-check")!.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("code checked each one", page.QuerySelector(".detail-check")!.TextContent);
    }

    [Fact]
    public async Task OnlyTheFirstThreeQuotesShowButEveryQuoteIsMarkedInTheSavedTextOfTheFold()
    {
        var page = await RenderAsync<StatementDetailPage, StatementPageData>(Statement());

        Assert.Equal(["First quote.", "Second quote.", "Third quote."], page.QuerySelectorAll(".detail-quotes blockquote").Select(quote => quote.TextContent));
        Assert.Contains("4 quotes are saved. The first 3 are shown.", page.QuerySelector("#statement-quotes-title")!.ParentElement!.TextContent);
        Assert.Equal("See all 4 quotes in the source fold", page.QuerySelector(".detail-section > p a")!.TextContent);
        var marked = page.QuerySelectorAll("#source mark").Select(mark => mark.TextContent).ToArray();
        Assert.Equal(["First quote.", "Second quote.", "Third quote.", "Fourth quote.", "Friday"], marked);
        Assert.Equal(["cite-1", "cite-2", "cite-3", "cite-4", StatementPageView.DateEvidenceAnchor], page.QuerySelectorAll("#source mark").Select(mark => mark.Id!).ToArray());
    }

    [Fact]
    public async Task EachShownQuoteLinksToItsMarkInTheSavedText()
    {
        var page = await RenderAsync<StatementDetailPage, StatementPageData>(Statement());

        var links = page.QuerySelectorAll(".detail-quotes a").Select(link => link.GetAttribute("href")!).ToArray();
        Assert.Equal(["#cite-1", "#cite-2", "#cite-3"], links);
        Assert.All(links, href => Assert.NotNull(page.QuerySelector("#source mark" + href)));
    }

    [Fact]
    public async Task ADatedStatementNamesItsDayAndAnUndatedOneSaysSoWithTheTimeTheSourceWasSeen()
    {
        var dated = await RenderAsync<StatementDetailPage, StatementPageData>(Statement());
        var undated = await RenderAsync<StatementDetailPage, StatementPageData>(Statement(dated: false));

        Assert.StartsWith("Said on 29 Oct 2026.", dated.QuerySelector(".detail-lede")!.TextContent);
        Assert.StartsWith("Date not stated. Source seen 29 Oct 2026, 17:19 UTC.", undated.QuerySelector(".detail-lede")!.TextContent);
        Assert.Contains("Not stated in the source", undated.QuerySelector("#source")!.TextContent);
        Assert.Null(undated.QuerySelector("#source mark[id=cite-statement-date]"));
    }

    [Fact]
    public async Task AModelFoundDateNamesItsEvidenceAndItsAiOriginInTheFold()
    {
        var page = await RenderAsync<StatementDetailPage, StatementPageData>(Statement());

        var fold = page.QuerySelector("#source")!.TextContent;
        Assert.Contains(PublishedRecordLabels.ModelDateCheckedByCode, fold);
        Assert.Contains("Evidence for the statement date", fold);
        Assert.Contains("Friday", fold);
    }

    [Fact]
    public async Task TheTrailEndsWithTheSpeakersAndTheOnlyReportLinkIsTheFooterLinkOfThisRevision()
    {
        var page = await RenderAsync<StatementDetailPage, StatementPageData>(Statement());

        Assert.Equal("Mark Kelly, Adam Schiff", page.QuerySelector(".public-breadcrumbs [aria-current=page]")!.TextContent);
        var reports = page.QuerySelectorAll("a[href*='report']");
        var report = Assert.Single(reports);
        Assert.Contains("Report a problem with this statement", report.TextContent);
        Assert.Contains("revision=2", report.GetAttribute("href"));
        Assert.NotNull(report.Closest(".public-footer"));
    }

    [Fact]
    public async Task SpeakersAndIssuesLinkToTheirPagesAndStoredTextIsEscaped()
    {
        var page = await RenderAsync<StatementDetailPage, StatementPageData>(Statement());

        Assert.Equal(Root + "officials/official-a.html", page.QuerySelector(".detail-list a")!.GetAttribute("href"));
        Assert.Equal(Root + PublicationProtocol.IssuePagePath("budget", 1), page.QuerySelector(".detail-chip")!.GetAttribute("href"));
        Assert.Equal("A model wrote this <b>summary</b>.", page.QuerySelector(".detail-ai p")!.TextContent);
        Assert.Empty(page.QuerySelectorAll(".detail-ai b"));
    }
}
