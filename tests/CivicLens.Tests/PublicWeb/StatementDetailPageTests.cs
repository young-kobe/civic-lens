using AngleSharp.Dom;
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

        var summary = page.QuerySelector(".ai-summary-ai")!;
        Assert.Equal(StatementLabel, summary.QuerySelector(".ai-label")!.TextContent);
        Assert.Contains("A model wrote only this summary.", summary.QuerySelector(".ai-summary-check")!.TextContent);
        Assert.Contains(StatementPageView.CaptureCaveat, summary.QuerySelector(".ai-summary-check")!.TextContent);
        Assert.Equal("#source", summary.QuerySelector(".ai-summary-check a")!.GetAttribute("href"));
        Assert.Null(page.QuerySelector(".caveat"));
    }

    [Fact]
    public async Task AShortReleaseShowsItsOwnSourceTextWithEachQuoteMarkedAndTheSourceTextLabelInPlaceOfASummary()
    {
        var data = Statement(PublishedTextOrigin.Code, dated: false) with { Summary = StatementText, Label = AiLabelWords.StatementSourceTextWording };

        var page = await RenderAsync<StatementDetailPage, StatementPageData>(data);

        var summary = page.QuerySelector(".ai-summary")!;
        Assert.Null(page.QuerySelector(".ai-summary-ai"));
        Assert.Equal(AiLabelWords.StatementSourceTextWording, summary.QuerySelector(".ai-label")!.TextContent);
        Assert.Equal(StatementText, summary.QuerySelector(".doc-text")!.TextContent);
        Assert.Equal(["First quote.", "Second quote.", "Third quote.", "Fourth quote."], summary.QuerySelectorAll("mark").Select(mark => mark.TextContent));
        Assert.Equal(1, page.QuerySelectorAll("[id='cite-1']").Length);
        Assert.Null(page.QuerySelector("#source .doc-text"));
        Assert.DoesNotContain("model", summary.TextContent, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ALongReleaseKeepsItsAiSummaryAndTheMarkedSourceTextStaysInTheFold()
    {
        var page = await RenderAsync<StatementDetailPage, StatementPageData>(Statement());

        Assert.Equal("A model wrote this <b>summary</b>.", page.QuerySelector(".ai-summary-ai p")!.TextContent);
        Assert.Null(page.QuerySelector(".ai-summary .doc-text"));
        Assert.Equal(StatementText, page.QuerySelector("#source .doc-text")!.TextContent);
    }

    [Fact]
    public async Task ModelSummaryExplainsExactTermsAndKeepsWrittenTextAndUnsafeMarkupAsText()
    {
        const string summary = "bill BILL bills billing _Bill 2Bill Bill2 e\u0301Bill \U00010400Bill Bill\u0301 <img src=x onerror=alert(1)>";
        var page = await RenderAsync<StatementDetailPage, StatementPageData>(Statement() with { Summary = summary });
        var summaryElement = page.QuerySelector(".ai-summary-ai p")!;

        Assert.Equal(summary, VisibleText(summaryElement));
        Assert.Empty(summaryElement.QuerySelectorAll("img"));
        Assert.Single(summaryElement.QuerySelectorAll("button.term"));
        Assert.Equal("bill", summaryElement.QuerySelector("button.term")!.TextContent);
        Assert.Single(summaryElement.QuerySelectorAll("[popover]"));
        Assert.Equal("term-bill", summaryElement.QuerySelector("[popover]")!.Id);
        Assert.Equal(StatementText, page.QuerySelector("#source .doc-text")!.TextContent);
        Assert.Equal(["First quote.", "Second quote.", "Third quote."], page.QuerySelectorAll(".detail-quotes blockquote").Select(quote => quote.TextContent));
    }

    [Theory]
    [InlineData(PublishedTextOrigin.Code)]
    [InlineData(PublishedTextOrigin.Person)]
    public async Task NonModelStatementTextDoesNotReceiveGlossaryPopoverMarkup(PublishedTextOrigin origin)
    {
        var page = await RenderAsync<StatementDetailPage, StatementPageData>(Statement(origin) with { Summary = "Bill" });

        var summary = page.QuerySelector(".ai-summary")!;
        Assert.Empty(summary.QuerySelectorAll("button.term"));
        var prose = origin == PublishedTextOrigin.Code ? summary.QuerySelector(".doc-text")! : summary.QuerySelector("p")!;
        Assert.Equal(origin == PublishedTextOrigin.Code ? StatementText : "Bill", prose.TextContent);
    }

    [Fact]
    public async Task APersonSummaryHasNoAiLabelAndNeverSaysAModelWroteIt()
    {
        var page = await RenderAsync<StatementDetailPage, StatementPageData>(Statement(PublishedTextOrigin.Person));

        Assert.Null(page.QuerySelector(".ai-summary-ai"));
        Assert.Null(page.QuerySelector(".ai-label"));
        Assert.DoesNotContain("model", page.QuerySelector(".ai-summary-check")!.TextContent, StringComparison.OrdinalIgnoreCase);
        Assert.Contains("code checked each one", page.QuerySelector(".ai-summary-check")!.TextContent);
        Assert.Contains(StatementPageView.CaptureCaveat, page.QuerySelector(".ai-summary-check")!.TextContent);
    }

    [Fact]
    public async Task ThePageShowsTheLabelOfTheViewAndNoLabelWhenTheViewSetsNone()
    {
        var withLabel = await RenderAsync<StatementDetailPage, StatementPageData>(Statement());
        var withoutLabel = await RenderAsync<StatementDetailPage, StatementPageData>(Statement(label: null, approver: PublishedApprover.Person));

        Assert.Equal(StatementLabel, withLabel.QuerySelector(".ai-label")!.TextContent);
        Assert.Null(withoutLabel.QuerySelector(".ai-label"));
        Assert.Null(withoutLabel.QuerySelector(".ai-summary-ai"));
    }

    [Fact]
    public async Task OnlyACodeApprovedDateNamesTheCodeCheckOnTheSharedLabelRule()
    {
        var code = await RenderAsync<StatementDetailPage, StatementPageData>(Statement());
        var person = await RenderAsync<StatementDetailPage, StatementPageData>(Statement(label: null, approver: PublishedApprover.Person));

        Assert.Contains(PublishedRecordLabels.ModelDateCheckedByCode, code.QuerySelector("#source")!.TextContent);
        Assert.DoesNotContain(PublishedRecordLabels.ModelDateCheckedByCode, person.QuerySelector("#source")!.TextContent);
        Assert.Contains("29 Oct 2026", person.QuerySelector("#source")!.TextContent);
    }

    [Fact]
    public async Task EachQuoteLinkNamesItsNumberOnlyWhenThereIsMoreThanOneQuote()
    {
        var one = await RenderAsync<StatementDetailPage, StatementPageData>(Statement(quotes: 1));
        var many = await RenderAsync<StatementDetailPage, StatementPageData>(Statement());

        Assert.Equal(": statement, quote", one.QuerySelector(".detail-quotes a .visually-hidden")!.TextContent);
        Assert.Equal(": statement, quote 1", many.QuerySelector(".detail-quotes a .visually-hidden")!.TextContent);
    }

    private static string VisibleText(IElement element) => string.Concat(element.ChildNodes
        .Where(node => node is not IElement child || !child.HasAttribute("popover"))
        .Select(node => node.TextContent));

    [Fact]
    public async Task OnlyTheFirstThreeQuotesShowButEveryQuoteIsMarkedInTheSavedTextOfTheFold()
    {
        var page = await RenderAsync<StatementDetailPage, StatementPageData>(Statement());

        Assert.Equal(["First quote.", "Second quote.", "Third quote."], page.QuerySelectorAll(".detail-quotes blockquote").Select(quote => quote.TextContent));
        Assert.Contains("4 quotes are saved. The first 3 are shown.", page.QuerySelector("#statement-quotes-title")!.ParentElement!.TextContent);
        var allQuotes = page.QuerySelector(".detail-section > p a")!;
        Assert.Equal("See all 4 quotes in the source fold", allQuotes.TextContent);
        Assert.Equal("#source", allQuotes.GetAttribute("href"));
        var marked = page.QuerySelectorAll("#source mark").Select(mark => mark.TextContent).ToArray();
        Assert.Equal(["First quote.", "Second quote.", "Third quote.", "Fourth quote.", "Friday"], marked);
        Assert.Equal(["cite-1", "cite-2", "cite-3", "cite-4", StatementPageView.DateEvidenceAnchor], page.QuerySelectorAll("#source mark").Select(mark => mark.Id!).ToArray());
    }

    [Theory]
    [InlineData(4, "See quote 4 in the source text above")]
    [InlineData(5, "See quotes 4 to 5 in the source text above")]
    public async Task AShortReleaseLinksTheQuotesLeftOutOfTheListToTheFirstOneInItsInlineSourceText(int quotes, string wording)
    {
        var data = Statement(PublishedTextOrigin.Code, dated: false, quotes: quotes) with { Summary = StatementText, Label = AiLabelWords.StatementSourceTextWording };

        var page = await RenderAsync<StatementDetailPage, StatementPageData>(data);

        var allQuotes = page.QuerySelector(".detail-section > p a")!;
        Assert.Equal(wording, allQuotes.TextContent);
        Assert.Equal("#cite-4", allQuotes.GetAttribute("href"));
        Assert.Equal("Fourth quote.", page.QuerySelector(".ai-summary mark#cite-4")!.TextContent);
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
        Assert.Equal(Root + PublicationProtocol.IssuePagePath("budget", 1), page.QuerySelector(".link-chip")!.GetAttribute("href"));
        Assert.Equal("A model wrote this <b>summary</b>.", page.QuerySelector(".ai-summary-ai p")!.TextContent);
        Assert.Empty(page.QuerySelectorAll(".ai-summary-ai b"));
    }

    [Fact]
    public async Task EachSpeakerShowsTheSeatAndThePartyOfTheTermAndOneSpeakerAlsoGetsThemInTheEyebrow()
    {
        var two = await RenderAsync<StatementDetailPage, StatementPageData>(Statement());
        var one = Statement();
        var single = await RenderAsync<StatementDetailPage, StatementPageData>(one with { Speakers = [one.Speakers[0]] });
        var noTerm = await RenderAsync<StatementDetailPage, StatementPageData>(one with { Speakers = [one.Speakers[1]] });

        var speakers = two.QuerySelectorAll("[aria-labelledby=statement-speakers-title] li").ToArray();
        Assert.Equal("Senator for AZ, Democrat", speakers[0].QuerySelector("p")!.TextContent);
        Assert.Null(speakers[1].QuerySelector("p"));
        Assert.Equal("Statement | Mark Kelly, Adam Schiff", two.QuerySelector(".detail-eyebrow")!.TextContent);
        Assert.Equal("Statement | Mark Kelly, Senator for AZ, Democrat", single.QuerySelector(".detail-eyebrow")!.TextContent);
        Assert.Equal("Statement | Adam Schiff", noTerm.QuerySelector(".detail-eyebrow")!.TextContent);
        Assert.Equal("Senator for AZ", (one.Speakers[0] with { Party = null }).Role);
    }

    [Fact]
    public async Task TheFoldShowsTheApprovalAndTheFirstPublicationTimesWithTheBriefLabelsAndNoRowForAMissingTime()
    {
        var page = await RenderAsync<StatementDetailPage, StatementPageData>(Statement());
        var missing = await RenderAsync<StatementDetailPage, StatementPageData>(Statement() with { ApprovedAtUtc = null });

        static (string, string)[] Facts(AngleSharp.Dom.IDocument document) => [.. document.QuerySelectorAll("#source dt")
            .Zip(document.QuerySelectorAll("#source dd"), (name, value) => (name.TextContent.Trim(), value.TextContent.Trim()))
            .Where(fact => fact.Item1 is "Approved" or "First published" or "Revision")];
        Assert.Equal([("Approved", "30 Oct 2026, 00:42 UTC"), ("First published", "30 Oct 2026, 00:44 UTC"), ("Revision", "2")], Facts(page));
        Assert.Equal([("First published", "30 Oct 2026, 00:44 UTC"), ("Revision", "2")], Facts(missing));
    }

    [Fact]
    public async Task TheLedeAndTheSourceCardNameTheHostAndTheFullUrlStaysInTheFold()
    {
        var page = await RenderAsync<StatementDetailPage, StatementPageData>(Statement());

        var lede = page.QuerySelector(".detail-lede a")!;
        Assert.Equal(("kelly.example", "https://kelly.example/release"), (lede.TextContent, lede.GetAttribute("href")!));
        var card = page.QuerySelector("[aria-labelledby=statement-source-title]")!;
        Assert.Equal("Source", card.QuerySelector("h2")!.TextContent);
        Assert.Equal(("kelly.example", "https://kelly.example/release"), (card.QuerySelector("a")!.TextContent, card.QuerySelector("a")!.GetAttribute("href")!));
        Assert.Equal("Source seen 29 Oct 2026, 17:19 UTC.", card.QuerySelector("p.muted")!.TextContent);
        Assert.Contains("https://kelly.example/release", page.QuerySelector("#source dd a")!.TextContent);
    }

    [Fact]
    public async Task TheRailLabelNamesTheSpeakersTheIssuesAndTheSourceCardsItHolds()
    {
        var page = await RenderAsync<StatementDetailPage, StatementPageData>(Statement());

        Assert.Equal("Speakers, issues, and source", page.QuerySelector("aside.detail-rail")!.GetAttribute("aria-label"));
    }

    [Fact]
    public async Task TheCorrectionBannerFollowsTheHeadlineOnlyForAPersonCorrectionAndARoutineUpdateShowsTheMutedLineInTheHeader()
    {
        var corrected = await RenderAsync<StatementDetailPage, StatementPageData>(Statement() with { RevisionNumber = 4, Corrections = PublishedCorrectionListTests.History });
        var plain = await RenderAsync<StatementDetailPage, StatementPageData>(Statement());
        var updated = await RenderAsync<StatementDetailPage, StatementPageData>(Statement() with { Corrections = [PublishedUpdateLineTests.SourceUpdate] });

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
    public async Task TheStatementSummaryLabelIsDrawnByAiLabel()
    {
        AiLabelAssert.DrawnByAiLabel(await RenderAsync<StatementDetailPage, StatementPageData>(Statement()), StatementLabel);
    }
}
