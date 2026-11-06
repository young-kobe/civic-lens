using AngleSharp.Html.Parser;
using CivicLens.Application.Publication;
using CivicLens.Host.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Host;

public sealed class PublishedBoardPageTests
{
    private static readonly OfficialBoard Board = new(PublishedChamber.House, PublishedPartyMajority.Democrat);

    [Fact]
    public async Task EveryCountNamesWhoAndWhatAndShowsItsTotalSoNoNumberStandsAlone()
    {
        var document = await RenderAsync(Page(Member("Ada Rep", 120, 14, missed: 12, held: 400)));

        var text = document.QuerySelector("main")!.TextContent;
        Assert.Contains("14 of 120 split votes (12%)", text);
        Assert.Contains("106 of 120 split votes (88%)", text);
        Assert.Contains("Missed 12 of the 400 roll call votes of the House of Representatives on this site (3%)", text);
        Assert.Contains("Voted against most House Democrats", text);
        Assert.Contains("Voted with most House Democrats", text);
    }

    [Fact]
    public async Task AMemberBelowTheMinimumIsNamedWithTheCountAndGetsNoRank()
    {
        var document = await RenderAsync(Page(Member("Short Record", 24, 12, missed: 1, held: 100), Member("Long Record", 25, 5, missed: 1, held: 100)));

        var tables = document.QuerySelectorAll("table").Where(table => table.GetAttribute("data-default-order")!.Contains("share")).ToList();
        Assert.All(tables, table => Assert.DoesNotContain("Short Record", table.TextContent));
        Assert.All(tables, table => Assert.Contains("Long Record", table.TextContent));
        var tooFew = document.QuerySelector("[aria-labelledby='panel-too-few-split-votes-yet']")!.TextContent;
        Assert.Contains("Short Record", tooFew);
        Assert.Contains("24 split votes cast", tooFew);
    }

    [Fact]
    public async Task TheSplitVoteTablesSortByTheShareBehindTheCellAndNameTheirDefaultOrder()
    {
        var document = await RenderAsync(Page(Member("Ada Rep", 100, 10, missed: 1, held: 100), Member("Bo Rep", 100, 40, missed: 1, held: 100)));

        var breakers = document.QuerySelector("[aria-labelledby='panel-voted-against-most-house-democrats']")!;
        Assert.Equal(["Bo Rep", "Ada Rep"], breakers.QuerySelectorAll("tbody th a, tbody td a").Select(link => link.TextContent));
        Assert.Equal(["0.4000", "0.1000"], breakers.QuerySelectorAll("tbody td[data-sort-value]").Select(cell => cell.GetAttribute("data-sort-value")));
        Assert.Contains("Highest share first", breakers.QuerySelector("caption")!.TextContent);
        Assert.Equal(["Official", "Share of split votes against most House Democrats"], breakers.QuerySelectorAll("thead th").Select(header => header.TextContent));
    }

    [Fact]
    public async Task TheLeftOutPanelNamesTheCongressOfTheFiguresSoAReaderKnowsWhichOfficialsAreNotInIt()
    {
        var document = await RenderAsync(new OfficialBoardPage(Board, 118, [], 0, 2, 0));

        Assert.Contains("Three lists of the House Democrats in the 118th Congress.", document.QuerySelector("main")!.TextContent);
        Assert.Contains("2 officials with no vote figures in this chamber in the 118th Congress.", document.QuerySelector("[aria-labelledby='panel-what-this-page-leaves-out']")!.TextContent);
    }

    [Fact]
    public async Task AKeptBoardSaysWhyItShowsTheEarlierCongressNamesTheCongressOfTheRollCallAndLinksThePagesOfThatCongress()
    {
        var page = new OfficialBoardPage(Board, 118, [Member("Ada Rep", 30, 3, missed: 1, held: 400, earlierPage: true)], 0, 0, 0, 119);

        var document = await RenderAsync(page);

        var text = document.QuerySelector("main")!.TextContent;
        Assert.Contains("The 119th Congress has no member with 25 split votes yet, so this page keeps the 118th Congress.", text);
        Assert.Contains("The newest roll call counted here, in the 118th Congress, is on", text);
        Assert.Contains("No count includes a later day.", text);
        var links = document.QuerySelectorAll("main table a").Select(link => link.GetAttribute("href")).Distinct().ToList();
        Assert.Equal(["officials/ada-rep/118.html"], links);
    }

    [Fact]
    public async Task ABoardAtTheNewestCongressHasNoKeptNoticeAndLinksTheCurrentPage()
    {
        var document = await RenderAsync(Page(Member("Ada Rep", 30, 3, missed: 1, held: 400)));

        Assert.DoesNotContain("keeps the", document.QuerySelector("main")!.TextContent);
        Assert.Equal(["officials/ada-rep.html"], document.QuerySelectorAll("main table a").Select(link => link.GetAttribute("href")).Distinct());
    }

    [Fact]
    public async Task APageWithNoCongressSaysSoAndStillListsWhatItLeavesOut()
    {
        var document = await RenderAsync(new OfficialBoardPage(Board, null, [], 8, 0, 0));

        Assert.Empty(document.QuerySelectorAll("table"));
        var text = document.QuerySelector("main")!.TextContent;
        Assert.Contains("No vote figures are in this release yet.", text);
        Assert.Contains("8 officials with no vote figures in this release", document.QuerySelector("[aria-labelledby='panel-what-this-page-leaves-out']")!.TextContent);
        Assert.DoesNotContain("no roll call votes", text, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task TheTabsLinkEveryBoardAndMarkOnlyTheCurrentOne()
    {
        var document = await RenderAsync(Page());

        var tabs = document.QuerySelectorAll("nav[aria-label^='Show the voting boards'] a").ToList();
        Assert.Equal(["board-house-democrat.html", "board-house-republican.html", "board-senate-democrat.html", "board-senate-republican.html"],
            tabs.Select(tab => tab.GetAttribute("href")));
        Assert.Equal(["House Democrats"], tabs.Where(tab => tab.GetAttribute("aria-current") is not null).Select(tab => tab.TextContent.Trim()));
    }

    [Fact]
    public async Task TheClosestVoteCardNamesTheDatesTheGapAndTheSizeOfThePoolAndLinksTheVote()
    {
        var closest = ClosestVote.From(ReleaseOf(Vote('a', 216, 214, PublishedVoteAlignment.Split), Vote('b', 300, 100, PublishedVoteAlignment.PartiesAgree)))!;

        var document = await RenderAsync(Page(), closest);

        Assert.Contains("Closest vote, 5 May 2026 to 11 May 2026", document.QuerySelectorAll("h2").Select(heading => heading.TextContent));
        Assert.Contains("The Yes and No counts differ by 2 votes.", document.Body!.TextContent, StringComparison.Ordinal);
        Assert.Contains("closest of 2 substantive roll call votes with counts", document.Body.TextContent, StringComparison.Ordinal);
        Assert.NotNull(document.QuerySelector($"a[href='records/{new string('a', 64)}.html']"));
    }

    [Fact]
    public async Task TheClosestVoteCardShowsTheBillLineOfItsVoteWithTheSourceNoteAndNoneWhenTheVoteHoldsNoLine()
    {
        var closest = ClosestVote.From(ReleaseOf(Vote('a', 216, 214, PublishedVoteAlignment.Split)))!;

        var shown = await RenderAsync(Page(), closest with { BillLine = "This bill funds rent aid." });
        var bare = await RenderAsync(Page(), closest);

        Assert.Equal(["This bill funds rent aid."], shown.QuerySelectorAll(".bill-line").Select(line => line.TextContent));
        Assert.Contains("AI summary of the Congressional Research Service summary.", shown.Body!.TextContent, StringComparison.Ordinal);
        Assert.Empty(bare.QuerySelectorAll(".bill-line"));
        Assert.DoesNotContain("AI summary of the Congressional Research Service summary.", bare.Body!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ATiedClosestVoteSaysTheCountsAreEqualAndNoVoteInTheWindowHidesTheCard()
    {
        var tied = ClosestVote.From(ReleaseOf(Vote('a', 50, 50, PublishedVoteAlignment.Mixed)))!;
        var stale = ClosestVote.From(ReleaseOf(Vote('a', 216, 214, PublishedVoteAlignment.Split) with { EventDate = new DateOnly(2026, 1, 1) }));

        Assert.Contains("The Yes and No counts are equal.", (await RenderAsync(Page(), tied)).Body!.TextContent, StringComparison.Ordinal);
        Assert.Null(stale);
        Assert.DoesNotContain("Closest vote", (await RenderAsync(Page(), stale)).Body!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheAgreementPanelStatesEveryCountWithItsDenominatorAndSitsBelowTheClosestVoteCard()
    {
        var closest = ClosestVote.From(ReleaseOf(Vote('a', 216, 214, PublishedVoteAlignment.Split)))!;
        var agreement = AgreementOf(Agreed(PublishedVoteKind.Passage, 3), Split(PublishedVoteKind.Passage, 1), Agreed(PublishedVoteKind.Amendment, 1), Mixed(PublishedVoteKind.Amendment, 1));

        var document = await RenderAsync(Page(), closest, agreement);

        var panel = document.QuerySelector("[aria-labelledby='panel-when-both-parties-agreed-(119th-congress,-house)']")!;
        var text = panel.TextContent;
        Assert.Contains("On 4 of the 6 substantive House roll call votes on this site, most Democrats and most Republicans who cast Yes or No voted the same way.", text);
        Assert.Contains("That the parties voted the same way is about the question of each vote. It does not show that they agree on an issue.", text);
        Assert.Contains("On 1 of the 6, the two parties took opposite sides.", text);
        Assert.Contains("On 1 of the 6, one party had no majority, as in a tie.", text);
        Assert.Equal(["Kind of vote", "Both parties agreed"], panel.QuerySelectorAll("thead th").Select(header => header.TextContent));
        Assert.Equal([("Vote to pass or agree to", "3 of 4"), ("Vote to change the text", "1 of 2"), ("All substantive votes", "4 of 6")],
            panel.QuerySelectorAll("tbody tr").Select(row => (row.Children[0].TextContent.Trim(), row.Children[1].TextContent.Trim())));
        Assert.Equal("method.html#parties-agreed", panel.QuerySelector("a")!.GetAttribute("href"));
        Assert.True(document.QuerySelector("[aria-labelledby^='panel-closest-vote']")!.CompareDocumentPosition(panel).HasFlag(AngleSharp.Dom.DocumentPositions.Following));
    }

    [Fact]
    public async Task TheAgreementPanelIsHiddenWithoutACountAndNamesAChamberWithNoAlignedVote()
    {
        var hidden = await RenderAsync(Page());
        var empty = await RenderAsync(Page(), agreement: AgreementOf());

        Assert.DoesNotContain("When both parties agreed", hidden.Body!.TextContent, StringComparison.Ordinal);
        Assert.Contains("This site has no substantive House roll call vote with both parties in the 119th Congress.", empty.Body!.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain(empty.QuerySelectorAll("table"), table => table.TextContent.Contains("Both parties agreed"));
    }

    [Fact]
    public async Task TheNominationNoteShowsOnlyWhenNominationsAreMoreThanHalfOfTheTotal()
    {
        var most = AgreementOf(Agreed(PublishedVoteKind.Nomination, 3), Agreed(PublishedVoteKind.Passage, 2), Split(PublishedVoteKind.Passage, 0));
        var half = AgreementOf(Agreed(PublishedVoteKind.Nomination, 2), Agreed(PublishedVoteKind.Passage, 2));

        Assert.Contains("Most of these 5 votes are on nominations. Look at each kind of vote, not only the total.", (await RenderAsync(Page(), agreement: most)).Body!.TextContent, StringComparison.Ordinal);
        Assert.DoesNotContain("are on nominations", (await RenderAsync(Page(), agreement: half)).Body!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task NoPercentShowsInTheAgreementPanelAndAOneVoteLeadUsesTheSingular()
    {
        var many = await RenderAsync(Page(), agreement: AgreementOf(Agreed(PublishedVoteKind.Passage, 3), Split(PublishedVoteKind.Passage, 1)));
        var one = await RenderAsync(Page(), agreement: AgreementOf(Agreed(PublishedVoteKind.Passage, 1)));

        Assert.DoesNotContain("%", many.QuerySelector("[aria-labelledby^='panel-when-both-parties-agreed']")!.TextContent);
        Assert.Contains("On 1 of the 1 substantive House roll call vote on this site", one.Body!.TextContent, StringComparison.Ordinal);
    }

    private static ChamberAgreement AgreementOf(params KindAgreement[] rows) => new(PublishedChamber.House, 119,
        [.. rows.GroupBy(row => row.Kind).Select(group => new KindAgreement(group.Key, group.Sum(row => row.Agreed), group.Sum(row => row.Split), group.Sum(row => row.Mixed)))]);

    private static KindAgreement Agreed(PublishedVoteKind kind, int count) => new(kind, count, 0, 0);

    private static KindAgreement Split(PublishedVoteKind kind, int count) => new(kind, 0, count, 0);

    private static KindAgreement Mixed(PublishedVoteKind kind, int count) => new(kind, 0, 0, count);

    private static readonly DateTimeOffset Published = new(2026, 5, 11, 8, 0, 0, TimeSpan.Zero);

    private static PublicationRelease ReleaseOf(params PublishedRecordEntry[] entries) => new()
    {
        SchemaVersion = PublicationProtocol.SchemaVersion,
        ReleaseNumber = 3,
        PublishedAtUtc = Published,
        Withdrawals = [],
        Records = entries
    };

    private static PublishedRecordEntry Vote(char id, int yea, int nay, PublishedVoteAlignment alignment) => new()
    {
        Kind = PublishedRecordKind.Vote,
        RecordId = new string(id, 64),
        RevisionNumber = 1,
        Headline = $"Headline {id}",
        FirstPublishedAtUtc = Published,
        Officials = [],
        IssueIds = [],
        PolicyAreaIds = [],
        YeaCount = yea,
        NayCount = nay,
        Alignment = alignment,
        EventDate = new DateOnly(2026, 5, 7)
    };

    private static async Task<AngleSharp.Dom.IDocument> RenderAsync(OfficialBoardPage page, ClosestVote? closestVote = null, ChamberAgreement? agreement = null) =>
        new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderBoardAsync(page, closestVote, agreement, CancellationToken.None));

    private static OfficialBoardPage Page(params BoardMember[] members) => new(Board, 119, [.. members], 0, 0, 0);

    private static BoardMember Member(string name, int cast, int breaks, int missed, int held, bool earlierPage = false)
    {
        var official = new OfficialListing(new PublishedOfficialProfile { Id = name.ToLowerInvariant().Replace(' ', '-'), Name = name, Terms = [] }, 0);
        return new(official, cast, breaks, new MissedVotes(PublishedChamber.House, missed, held, new DateOnly(2025, 1, 3), new DateOnly(2026, 10, 27)),
            earlierPage ? PublicationProtocol.OfficialCongressPagePath(official.Profile.Id, 118) : official.PagePath);
    }

    [Theory]
    [InlineData(1, 400, "1 of 400 split votes (under 1%)")]
    [InlineData(0, 400, "0 of 400 split votes (0%)")]
    [InlineData(399, 400, "399 of 400 split votes (over 99%)")]
    [InlineData(400, 400, "400 of 400 split votes (100%)")]
    [InlineData(14, 120, "14 of 120 split votes (12%)")]
    [InlineData(1, 150, "1 of 150 split votes (1%)")]
    [InlineData(149, 150, "149 of 150 split votes (99%)")]
    [InlineData(199, 200, "199 of 200 split votes (over 99%)")]
    public async Task ASplitVoteCountNeverRoundsANonzeroCountToZeroOrANonTotalToAHundredPercent(int breaks, int cast, string expected)
    {
        var document = await RenderAsync(Page(Member("Ada Rep", cast, breaks, missed: 1, held: 100)));

        var cell = document.QuerySelector("[aria-labelledby='panel-voted-against-most-house-democrats'] tbody td:last-child")!;
        Assert.Equal(expected, cell.TextContent.Trim());
    }

    [Fact]
    public async Task AMissedShareBelowOneHalfPercentReadsUnderOnePercentSoANonzeroCountNeverReadsZero()
    {
        var document = await RenderAsync(Page(Member("Ada Rep", 30, 3, missed: 1, held: 400)));

        Assert.Contains("(under 1%)", document.QuerySelector("[aria-labelledby='panel-missed-votes-of-the-house-democrats']")!.TextContent);
    }

    [Fact]
    public async Task TheShareAndMissedColumnsNameTheirShortSortLabelAndMarkTheDefaultOrderSoOneOrderHasOneName()
    {
        var document = await RenderAsync(Page(Member("Ada Rep", 30, 3, missed: 1, held: 400)));

        var shareHeader = document.QuerySelector("[aria-labelledby='panel-voted-against-most-house-democrats'] thead th[data-sort=number]")!;
        Assert.Equal(("Share", ""), (shareHeader.GetAttribute("data-sort-label"), shareHeader.GetAttribute("data-sort-default")));
        Assert.Equal("Share of split votes against most House Democrats", shareHeader.TextContent);
        var officialHeader = document.QuerySelector("[aria-labelledby='panel-voted-against-most-house-democrats'] thead th[data-sort=text]")!;
        Assert.Null(officialHeader.GetAttribute("data-sort-default"));
        Assert.Equal("", document.QuerySelector("[aria-labelledby='panel-missed-votes-of-the-house-democrats'] thead th[data-sort=number]")!.GetAttribute("data-sort-default"));
    }
}
