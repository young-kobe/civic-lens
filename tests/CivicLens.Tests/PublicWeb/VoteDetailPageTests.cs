using CivicLens.Tests.Fixtures;
using CivicLens.Publication.Contracts;
using CivicLens.PublicWeb.Publication;
using static CivicLens.Tests.PublicWeb.VoteDetailPageFixture;

namespace CivicLens.Tests.PublicWeb;

public sealed class VoteDetailPageTests
{
    [Fact]
    public async Task TheAnswerComesFirstThenTheResultTheExplainersThePartyBarsAndTheMembers()
    {
        var page = await RenderAsync(Vote());

        var order = new[] { "h1.vote-headline", ".vote-summary", ".vote-result", ".vote-explain", ".vote-parties", ".vote-breakers", ".vote-members" }
            .Select(selector => Position(page, selector)).ToArray();

        Assert.All(order, position => Assert.True(position >= 0));
        Assert.Equal(order.Order(), order);
        Assert.Equal("The House passed H.R. 5334.", page.QuerySelector("h1")!.TextContent);
    }

    [Fact]
    public async Task EveryPartyGroupGetsOneBarWithItsWholePartyCountsAndAMajorityLineAndTheChamberBarIsNeutral()
    {
        var page = await RenderAsync(Vote());

        var bars = page.QuerySelectorAll(".vote-parties .party-bar");
        Assert.Equal(["House Democrats", "House Republicans", "House Independents"], bars.Select(bar => bar.QuerySelector("figcaption")!.TextContent));
        Assert.Equal(["democrat", "republican", "neutral"], bars.Select(bar => bar.QuerySelector(".party-bar-fill[data-fill=solid]")!.GetAttribute("data-tone")));
        Assert.Equal("House Democrats: Yes 1, No 3, Not voting 1", bars[0].QuerySelector("svg")!.GetAttribute("aria-label"));
        Assert.Contains("Half of Yes and No votes: 2", bars[0].TextContent);
        Assert.Contains("Half of Yes and No votes: 2.5", bars[1].TextContent);
        var chamber = page.QuerySelector(".vote-result .party-bar")!;
        Assert.Equal("neutral", chamber.QuerySelector(".party-bar-fill[data-fill=solid]")!.GetAttribute("data-tone"));
        Assert.Equal("House members: Yes 6, No 4, Present 1, Not voting 3", chamber.QuerySelector("svg")!.GetAttribute("aria-label"));
        Assert.Contains("14 members have a recorded position, including 3 who did not vote.", page.QuerySelector(".vote-result")!.TextContent);
        Assert.Contains("on 16 Sep 2026", page.QuerySelector(".vote-result")!.TextContent);
        Assert.Contains("Yes, No, Present, or Not voting", page.QuerySelector(".vote-result")!.TextContent);
    }

    [Theory]
    [InlineData(PublishedVoteKind.Passage, true)]
    [InlineData(PublishedVoteKind.Rule, true)]
    [InlineData(PublishedVoteKind.Nomination, true)]
    [InlineData(PublishedVoteKind.Suspension, false)]
    [InlineData(PublishedVoteKind.VetoOverride, false)]
    [InlineData(PublishedVoteKind.Cloture, false)]
    [InlineData(PublishedVoteKind.Other, false)]
    public async Task OnlyAVoteKnownToNeedASimpleMajorityDrawsTheHalfLineOnTheChamberBar(PublishedVoteKind kind, bool drawsLine)
    {
        var page = await RenderAsync(Vote() with { Kind = kind, KindCitation = null });

        var chamber = page.QuerySelector(".vote-result .party-bar")!;
        Assert.Equal(drawsLine, chamber.QuerySelector("line.party-bar-mark") is not null);
        Assert.Equal(drawsLine, chamber.TextContent.Contains("Half of Yes and No votes", StringComparison.Ordinal));
        Assert.NotNull(page.QuerySelector(".vote-parties line.party-bar-mark"));
    }

    [Fact]
    public async Task APassageThatMayAmendTheConstitutionDrawsNoHalfLineOnTheChamberBar()
    {
        var vote = Vote();
        var title = new PublishedVoteFact { Text = PublishedVote.TitlePrefix + "Proposing an amendment to the Constitution of the United States.", Citation = vote.Facts[0].Citation };

        var page = await RenderAsync(vote with { Facts = [.. vote.Facts, title] });

        Assert.Null(page.QuerySelector(".vote-result .party-bar line.party-bar-mark"));
        Assert.NotNull(page.QuerySelector(".vote-parties line.party-bar-mark"));
    }

    [Fact]
    public async Task TheLegendLineStatesTheCountWhereTheLineIsDrawn()
    {
        var page = await RenderAsync(Vote());

        var chamber = page.QuerySelector(".vote-result .party-bar")!;
        var line = chamber.QuerySelector("svg.party-bar-chart line.party-bar-mark")!;
        Assert.Equal("35.71%", line.GetAttribute("x1"));
        Assert.Contains("Half of Yes and No votes: 5", chamber.QuerySelector(".party-bar-legend")!.TextContent);
    }

    [Fact]
    public async Task UnknownPartyCountsShowNoPartyCardAndNoBreakersInsteadOfZeros()
    {
        var page = await RenderAsync(Vote() with { PartyTallies = null });

        Assert.Null(page.QuerySelector(".vote-parties"));
        Assert.Null(page.QuerySelector(".vote-breakers"));
        Assert.Equal("This record does not show party counts.", page.QuerySelector(".vote-parties-unknown")!.TextContent.Trim());
        Assert.Single(page.QuerySelectorAll(".party-bar"));
        Assert.DoesNotContain("Democrats", page.QuerySelector("main")!.TextContent);
    }

    [Fact]
    public async Task UnknownChamberTotalsShowTheResultWordWithoutABar()
    {
        var page = await RenderAsync(Vote() with { Totals = null });

        Assert.Equal("Passed Split vote", page.QuerySelector(".vote-result-word")!.TextContent);
        Assert.Null(page.QuerySelector(".vote-result .party-bar"));
    }

    [Fact]
    public async Task BreakersAreTrackedMembersWhoCastYesOrNoAgainstTheirPartyMajorityAndEachPartyHeadingLinksToItsBoard()
    {
        var page = await RenderAsync(Vote());

        var groups = page.QuerySelectorAll(".vote-breaker-group");
        Assert.Equal(2, groups.Length);
        Assert.Equal("House Democrats: 1 tracked member voted against most of the party", groups[0].QuerySelector("h3")!.TextContent);
        Assert.Equal(["Ada Able"], groups[0].QuerySelectorAll("li a").Select(link => link.TextContent));
        Assert.Equal(Root + "board-house-democrat.html", groups[0].QuerySelector("h3 a")!.GetAttribute("href"));
        Assert.Equal(["Di Dole"], groups[1].QuerySelectorAll("li a").Select(link => link.TextContent));
    }

    [Fact]
    public async Task AGroupWithNoBreakerShowsOneLineWithItsBoardLinkAndNoHeading()
    {
        var vote = Vote();
        var page = await RenderAsync(vote with { Members = [.. vote.Members.Where(member => member.Party != "Republican")] });

        var groups = page.QuerySelectorAll(".vote-breaker-group");
        Assert.Equal(2, groups.Length);
        Assert.NotNull(groups[0].QuerySelector("h3"));
        Assert.Null(groups[1].QuerySelector("h3"));
        Assert.Null(groups[1].QuerySelector("ul"));
        Assert.Equal("House Republicans: No tracked member of this party voted against most House Republicans.", groups[1].TextContent.Trim());
        Assert.Equal(Root + "board-house-republican.html", groups[1].QuerySelector("a")!.GetAttribute("href"));
        Assert.DoesNotContain("0 tracked", page.QuerySelector(".vote-breakers")!.TextContent);
    }

    [Fact]
    public async Task AVoteWhereThePartiesAgreedHasNoBreakersAndShowsItsBadge()
    {
        var page = await RenderAsync(Vote() with { Alignment = PublishedVoteAlignment.PartiesAgree });

        Assert.Null(page.QuerySelector(".vote-breakers"));
        Assert.Equal(VoteWords.PartiesAgreedBadge, page.QuerySelector(".vote-result .badge")!.TextContent);
    }

    [Fact]
    public async Task TheMemberTableIsFoldedAndFiltersByNameVoteAndParty()
    {
        var all = await RenderAsync(Vote());
        var byCast = await RenderAsync(Vote(), new(Cast: "Nay"));
        var byName = await RenderAsync(Vote(), new(Name: "dole"));
        var byParty = await RenderAsync(Vote(), new(Party: "Republican", Cast: "Aye"));
        var none = await RenderAsync(Vote(), new(Name: "zzz"));

        Assert.Null(all.QuerySelector(".vote-members details")!.GetAttribute("open"));
        Assert.Equal(5, Names(all).Length);
        Assert.Equal(["Ben Baker", "Di Dole"], Names(byCast));
        Assert.Equal(["Di Dole"], Names(byName));
        Assert.Equal(["Cy Cole"], Names(byParty));
        Assert.NotNull(byCast.QuerySelector(".vote-members details")!.GetAttribute("open"));
        Assert.Contains("2 of 5 tracked members shown.", byCast.QuerySelector(".vote-members")!.TextContent);
        Assert.Contains("No tracked member matches these filters.", none.QuerySelector(".vote-members")!.TextContent);
    }

    [Fact]
    public async Task EachMemberRowLinksToItsMarkedLineInTheSourceFoldThatPublicJsOpens()
    {
        var page = await RenderAsync(Vote());

        var anchors = page.QuerySelectorAll(".vote-members .source-line").Select(link => link.GetAttribute("href")![1..]).ToArray();
        Assert.Equal(5, anchors.Length);
        var fold = page.QuerySelector("details.vote-source")!;
        Assert.All(anchors, anchor =>
        {
            var target = page.GetElementById(anchor)!;
            Assert.Contains("Member:", target.TextContent);
            Assert.Same(fold, target.Closest("details"));
        });
        var script = File.ReadAllText(Path.Combine(RepositoryRoot.FullName, "src", "CivicLens.PublicWeb", "wwwroot", "js", "public.js"));
        Assert.Contains("closest(\"details\")", script, StringComparison.Ordinal);
        Assert.Contains("addEventListener(\"hashchange\", openFoldOfHash)", script, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheFilterFormAndTheClearLinkStayOnThePageThatServedTheVote()
    {
        var page = await RenderAsync(Vote(), new(Cast: "Nay"));

        Assert.Equal(PagePath + "#members", page.QuerySelector("form.vote-filter")!.GetAttribute("action"));
        Assert.Equal(PagePath + "#members", page.QuerySelector(".vote-filter-actions a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task ThePartyFilterOffersNoKnownPartyOnlyWhenAMemberHasNoPartyAndFindsThatMember()
    {
        var withoutParty = Vote() with { Members = [.. Members, new("o6", "Fay Fox", "Aye", null, Cite(Lines[4]))] };

        var offered = await RenderAsync(withoutParty, new(Party: VoteMemberFilter.NoKnownParty));
        var absent = await RenderAsync(Vote());

        var option = offered.QuerySelector("#vote-filter-party option[value=none]")!;
        Assert.Equal("No known party", option.TextContent);
        Assert.NotNull(option.GetAttribute("selected"));
        Assert.Equal(["Fay Fox"], Names(offered));
        Assert.Null(absent.QuerySelector("#vote-filter-party option[value=none]"));
    }

    [Fact]
    public async Task OneCheckTheSourceFoldHoldsTheCitedFactsAndTheSourceTextAndNoTimelineExists()
    {
        var page = await RenderAsync(Vote());

        var fold = Assert.Single(page.QuerySelectorAll("details.vote-source"));
        Assert.Equal("Check the source", fold.QuerySelector("summary")!.TextContent);
        Assert.Contains("Casts of all House members: Yes 6, No 4, Present 1, Not voting 3", fold.QuerySelector("dl.facts")!.TextContent);
        Assert.Equal(1, fold.QuerySelectorAll(".doc-text").Length);
        Assert.Null(page.QuerySelector(".timeline, #timeline-title"));
        Assert.DoesNotContain("Timeline", page.QuerySelector("main")!.TextContent);
    }

    [Fact]
    public async Task TheBillCardLinksTheBillAndLabelsItsLineAndIssueChipsLinkTheirPages()
    {
        var page = await RenderAsync(Vote());

        var bill = page.QuerySelector(".vote-bill")!;
        Assert.Equal(Root + "records/" + new string('b', 64) + ".html", bill.QuerySelector("a")!.GetAttribute("href"));
        Assert.Contains(PublishedWording.BillLineSource, bill.TextContent);
        Assert.Equal(Root + "issues/tax.html", page.QuerySelector(".vote-chip")!.GetAttribute("href"));
    }

    [Fact]
    public async Task ThePlainWordsOfTheApprovedWordingTableAnswerWhatYesMeantAndWhatHappensNext()
    {
        var page = await RenderAsync(Vote());

        var wording = VoteWording.Of(Vote().WordingSource)!.Value;
        var explain = page.QuerySelector(".vote-explain")!.TextContent;
        Assert.Contains(wording.Yes, explain);
        Assert.Contains(wording.No, explain);
        Assert.Contains(wording.NextStep, explain);
    }

    [Fact]
    public async Task AnUnsupportedQuestionShowsTheSharedUnknownNextStepSentence()
    {
        var page = await RenderAsync(Vote() with { KindCitation = null, Kind = PublishedVoteKind.Other });

        Assert.Contains(NextStep.NotShown, page.QuerySelector(".vote-explain")!.TextContent);
    }

    [Fact]
    public async Task TheSummaryKeepsItsAiLabelAndEscapesStoredText()
    {
        var page = await RenderAsync(Vote() with { Summary = "<script>x()</script>" });

        Assert.Equal(PublishedRecordLabels.ModelWording, page.QuerySelector(".vote-summary-label")!.TextContent);
        Assert.Equal("<script>x()</script>", page.QuerySelector(".vote-summary p")!.TextContent);
        Assert.Null(page.QuerySelector("main script"));
    }

    private static int Position(AngleSharp.Dom.IDocument page, string selector) =>
        page.QuerySelector(selector) is { } element ? page.All.ToList().IndexOf(element) : -1;

    private static string[] Names(AngleSharp.Dom.IDocument page) =>
        [.. page.QuerySelectorAll(".vote-members tbody tr td:first-child").Select(cell => cell.TextContent)];
}
