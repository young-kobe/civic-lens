using CivicLens.Application.Issues;
using AngleSharp.Html.Parser;
using CivicLens.Application.Publication;
using CivicLens.Host.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;

namespace CivicLens.Tests.Host;

public sealed class PublishedOfficialPageTests
{
    [Fact]
    public async Task TheCurrentPageListsStatementsActionsAndVotesNewestFirstWithTheirOwnDates()
    {
        var page = Current(
            [Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1)), Entry('2', PublishedRecordKind.Action, new DateOnly(2026, 5, 1))],
            [Vote('3', new DateOnly(2026, 4, 1)), Vote('4', new DateOnly(2025, 12, 1))]);

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var records = new HtmlParser().ParseDocument(html).QuerySelector("[aria-labelledby='panel-records']")!;
        Assert.Equal([new string('2', 64), new string('3', 64), new string('1', 64), new string('4', 64)],
            records.QuerySelectorAll("a.cell-title").Select(link => link.GetAttribute("href")!.Replace("../records/", "").Replace(".html", "")));
        Assert.Equal(["2026-05-01", "2026-04-01", "2026-03-01", "2025-12-01"], records.QuerySelectorAll("tbody td.num").Select(cell => cell.TextContent));
        Assert.Contains("House roll call 100, Congress 119, session 1, HR 7, Congress 119. Result of the vote: Passed", records.TextContent, StringComparison.Ordinal);
        Assert.Contains("Yea", records.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOfficialPageAndAFacetPageOrderTheSameEntriesTheSameWayBecauseOneRuleOwnsTheDateOrder()
    {
        var published = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
        PublishedRecordEntry[] entries =
        [
            Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1)),
            Entry('2', PublishedRecordKind.Action, new DateOnly(2026, 5, 1)) with { EventDate = null, FirstPublishedAtUtc = new DateTimeOffset(2026, 5, 2, 9, 0, 0, TimeSpan.Zero) },
            Entry('3', PublishedRecordKind.Statement, new DateOnly(2026, 5, 2)) with { EventDate = null, FirstPublishedAtUtc = new DateTimeOffset(2026, 5, 2, 21, 0, 0, TimeSpan.FromHours(-5)) },
            Entry('4', PublishedRecordKind.Action, new DateOnly(2026, 4, 1)),
            Entry('0', PublishedRecordKind.Action, new DateOnly(2026, 5, 1)) with { EventDate = null, FirstPublishedAtUtc = new DateTimeOffset(2026, 5, 2, 15, 0, 0, TimeSpan.Zero) }
        ];
        var facet = ReleaseFacets.From(new PublicationRelease
        {
            SchemaVersion = PublicationProtocol.SchemaVersion,
            ReleaseNumber = 1,
            PublishedAtUtc = published,
            Withdrawals = [],
            Records = [.. entries.Select(entry => entry with { IssueIds = ["housing"] })]
        }, PublicationCatalogs.None).Issues.Single();

        var official = await new ReleaseRenderer(null, null).RenderOfficialAsync(Current(entries, []), PublicationCatalogs.None, CancellationToken.None);
        var facetHtml = await new ReleaseRenderer(null, null).RenderFacetPageAsync(facet, 1, CancellationToken.None);

        string[] Order(string html) => [.. new HtmlParser().ParseDocument(html).QuerySelectorAll("tbody a.cell-title")
            .Select(link => link.GetAttribute("href")!.Replace("../records/", "").Replace(".html", ""))];
        Assert.Equal(Order(official), Order(facetHtml));
        Assert.Equal([new string('3', 64), new string('0', 64), new string('2', 64), new string('4', 64), new string('1', 64)], Order(official));
    }

    [Fact]
    public async Task AVoteFollowsTheRecordsOfItsDayBecauseAVoteRowHasNoPublicationTime()
    {
        var page = Current([Entry('a', PublishedRecordKind.Action, new DateOnly(2026, 5, 2))], [Vote('0', new DateOnly(2026, 5, 2))]);

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        Assert.Equal([new string('a', 64), new string('0', 64)], new HtmlParser().ParseDocument(html).QuerySelectorAll("tbody a.cell-title")
            .Select(link => link.GetAttribute("href")!.Replace("../records/", "").Replace(".html", "")));
    }

    [Fact]
    public async Task TheCurrentPageShowsTheNameTheOfficesAndTheDatedTermsWithTableCaptionsAndAnOngoingTermAsPresent()
    {
        var page = Current([], []) with
        {
            Profile = new PublishedOfficialProfile
            {
                Id = "mayor",
                Name = "Mayor Example",
                Terms =
                [
                    new() { Office = "rep", State = "WA", District = 1, Party = "Democrat", Start = new(1993, 1, 3), End = new(2025, 1, 3) },
                    new() { Office = "Secretary of State", Start = new(2025, 1, 21) }
                ]
            }
        };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        Assert.Equal("Mayor Example", document.QuerySelector("h1")!.TextContent);
        Assert.Equal(["Representative for WA, district 1", "Secretary of State"],
            document.QuerySelectorAll("[aria-labelledby='panel-offices'] tbody tr td:first-child").Select(cell => cell.TextContent));
        Assert.Equal(["2025-01-03", "Present"], document.QuerySelectorAll("[aria-labelledby='panel-offices'] tbody tr td.num:nth-child(4)").Select(cell => cell.TextContent));
        Assert.All(document.QuerySelectorAll("table"), table => Assert.NotNull(table.QuerySelector("caption")));
        Assert.Contains("href=\"../officials.html\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"../assets/css/theme.css\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCurrentPageLinksTheOfficialWebsiteNamingItsHostWithNoReferrerAndNoOpener()
    {
        var page = Current([], []) with { Profile = Profile() with { Website = "https://warren.senate.gov/about" } };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var link = new HtmlParser().ParseDocument(html).QuerySelector("a[href='https://warren.senate.gov/about']")!;
        Assert.Equal("Official website (warren.senate.gov)", link.TextContent);
        Assert.Equal("noopener noreferrer", link.GetAttribute("rel"));
    }

    [Fact]
    public async Task AnOfficialWithNoWebsiteShowsNoWebsiteLink()
    {
        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(Current([], []), PublicationCatalogs.None, CancellationToken.None);

        Assert.DoesNotContain("Official website", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheProfilePanelStatesTheMissedVotesOfEachChamberOnceWithTheMeaningAfterThem()
    {
        var day = new DateOnly(2026, 4, 1);
        var votes = new[] { Vote('a', day) with { Cast = PublishedOfficialVote.NotVoting }, Vote('b', day.AddDays(2)), Vote('c', day.AddDays(1)) with { Chamber = PublishedChamber.Senate } };
        var page = Current([], votes);
        page = page with { TalkAndAction = TalkAndActionProfile.Of(119, page.Records, page.Votes) };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var paragraphs = new HtmlParser().ParseDocument(html).QuerySelectorAll("[aria-labelledby='panel-talk-and-action-in-congress-119'] p.muted")
            .Select(paragraph => paragraph.TextContent.Trim()).ToArray();
        Assert.Equal(["Missed 1 of 2 roll call votes of all kinds, including procedural votes, held in the House of Representatives while they served, 1 Apr 2026 to 3 Apr 2026 (119th Congress).",
            "Missed 0 of 1 roll call vote of all kinds, including procedural votes, held in the Senate while they served, on 2 Apr 2026 (119th Congress).",
            "A missed vote means the official did not vote on it."], paragraphs[..3]);
    }

    [Fact]
    public async Task TheMissedVotesSentenceShowsTheShareOnlyAtTheThresholdAndAnOfficialWithNoRowsOfAChamberGetsNoSentenceForIt()
    {
        var votes = Enumerable.Range(0, 5).Select(index => Vote((char)('a' + index), new DateOnly(2026, 4, 1)) with
        { Cast = index == 0 ? PublishedOfficialVote.NotVoting : "Yea" }).ToArray();
        var page = Current([], votes);
        page = page with { TalkAndAction = TalkAndActionProfile.Of(119, page.Records, page.Votes) };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var text = new HtmlParser().ParseDocument(html).QuerySelector("[aria-labelledby='panel-talk-and-action-in-congress-119']")!.TextContent;
        Assert.Contains("Missed 1 of 5 roll call votes of all kinds (20%), including procedural votes, held in the House of Representatives", text, StringComparison.Ordinal);
        Assert.DoesNotContain("the Senate", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOfficialWhoMissedEveryRollCallOfAChamberShowsAHundredPercent()
    {
        var votes = Enumerable.Range(0, 5).Select(index => Vote((char)('a' + index), new DateOnly(2026, 4, 1).AddDays(index)) with { Cast = PublishedOfficialVote.NotVoting }).ToArray();
        var page = Current([], votes);
        page = page with { TalkAndAction = TalkAndActionProfile.Of(119, page.Records, page.Votes) };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var text = new HtmlParser().ParseDocument(html).QuerySelector("[aria-labelledby='panel-talk-and-action-in-congress-119']")!.TextContent;
        Assert.Contains("Missed 5 of 5 roll call votes of all kinds (100%), including procedural votes", text, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheVotesWithTheirPartyPanelStatesEachChamberOnceAndListsEveryBreakNewestFirstBetweenTheProfileAndTheRecords()
    {
        var day = new DateOnly(2026, 4, 1);
        var votes = new[] { Split('a', day, "Yea"), Split('b', day.AddDays(2), "Nay"), Split('c', day.AddDays(1), "No"), Split('d', day.AddDays(3), "Present"),
            Split('e', day, "Nay") with { Chamber = PublishedChamber.Senate, Party = PublishedPartyMajority.Democrat, PartyMajority = "Nay" } };
        var document = await RenderedAsync(votes);

        var panel = document.QuerySelector("[aria-labelledby='panel-votes-with-their-party']")!;
        Assert.Equal(["Voted with most House Republicans on 1 of 3 split votes in the 119th Congress. They did not cast Yea or Nay on 1 other split vote.",
            "Voted with most Senate Democrats on 1 of 1 split vote in the 119th Congress.",
            "A split vote is a substantive roll call vote where most Democrats and most Republicans voted on opposite sides."],
            panel.QuerySelectorAll("p").Select(paragraph => paragraph.TextContent.Trim()));
        var table = panel.QuerySelector("table")!;
        Assert.Equal(["Date", "Vote", "Their vote", "Most House Republicans", "Result"], table.QuerySelectorAll("th").Select(cell => cell.TextContent.Trim()));
        Assert.Equal(["2026-04-03", "2026-04-02"], table.QuerySelectorAll("tbody tr").Select(row => row.QuerySelector("td")!.TextContent.Trim()));
        var first = table.QuerySelector("tbody tr")!.QuerySelectorAll("td");
        Assert.Equal(["Vote b", "Nay", "Yea", "Passed"], first.Skip(1).Select(cell => cell.TextContent.Trim()));
        Assert.EndsWith(PublicationProtocol.RecordPagePath(new string('b', 64)), first[1].QuerySelector("a")!.GetAttribute("href"));
        Assert.Single(panel.QuerySelectorAll("table"));
        var titles = document.QuerySelectorAll("h2").Select(title => title.TextContent.Trim()).ToArray();
        Assert.True(Array.IndexOf(titles, "Talk and action in Congress 119") < Array.IndexOf(titles, "Votes with their party"));
        Assert.True(Array.IndexOf(titles, "Votes with their party") < Array.IndexOf(titles, "Records"));
    }

    [Fact]
    public async Task AnOfficialWhoNeverBrokeWithThePartyGetsOneLineAndNoTableAndAnOfficialWithNoSplitVoteGetsAPlainSentence()
    {
        var day = new DateOnly(2026, 4, 1);
        var matched = await RenderedAsync([Split('a', day, "Yea")]);
        var none = await RenderedAsync([Vote('a', day)]);

        var matchedPanel = matched.QuerySelector("[aria-labelledby='panel-votes-with-their-party']")!;
        Assert.Contains("No votes against most of their party.", matchedPanel.TextContent, StringComparison.Ordinal);
        Assert.Null(matchedPanel.QuerySelector("table"));
        var nonePanel = none.QuerySelector("[aria-labelledby='panel-votes-with-their-party']")!;
        Assert.DoesNotContain("No votes against most of their party.", nonePanel.TextContent, StringComparison.Ordinal);
        Assert.Contains("No Yea or Nay cast yet on a split vote in the House of Representatives as a Republican in the 119th Congress.", nonePanel.TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(4, "on 3 of 4 split votes in the 119th Congress.")]
    [InlineData(5, "on 4 of 5 split votes (80%) in the 119th Congress.")]
    public async Task ThePercentOfVotesWithTheirPartyShowsOnlyAtTheThreshold(int cast, string expected)
    {
        var votes = Enumerable.Range(0, cast).Select(index => Split((char)('a' + index), new DateOnly(2026, 4, 1).AddDays(index), index == 0 ? "Nay" : "Yea"));

        var document = await RenderedAsync([.. votes]);

        Assert.Contains(expected, document.QuerySelector("[aria-labelledby='panel-votes-with-their-party']")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOfficialWhoseRowsNameNeitherMajorPartyGetsNoVotesWithTheirPartyPanel()
    {
        var document = await RenderedAsync([Split('a', new DateOnly(2026, 4, 1), "Yea") with { Party = "Independent" }]);

        Assert.Null(document.QuerySelector("[aria-labelledby='panel-votes-with-their-party']"));
    }

    [Fact]
    public async Task OnlyARowOfASplitVoteCastAgainstMostOfTheirPartyIsMarkedAsAgainstTheParty()
    {
        var day = new DateOnly(2026, 4, 1);
        var votes = new[] { Split('a', day, "Nay"), Split('b', day.AddDays(1), "Yea"), Split('c', day.AddDays(2), "Present"),
            Vote('d', day.AddDays(3)) with { Cast = "Nay", Kind = PublishedVoteKind.Cloture }, Vote('e', day.AddDays(4)) with { Cast = "Nay", PartyMajority = null } };

        var document = await RenderedAsync(votes);

        var marked = document.QuerySelectorAll("[aria-labelledby='panel-records'] tbody tr.against-party");
        Assert.EndsWith(PublicationProtocol.RecordPagePath(Vote('a', day).RecordId), Assert.Single(marked).QuerySelector("a")!.GetAttribute("href"), StringComparison.Ordinal);
        Assert.Equal(5, document.QuerySelectorAll("[aria-labelledby='panel-records'] tbody tr").Length);
    }

    [Fact]
    public async Task TheRecordsSwitchShowsOnlyWhenTheOfficialHasAVoteAgainstTheirParty()
    {
        var day = new DateOnly(2026, 4, 1);

        var withBreak = await RenderedAsync([Split('a', day, "Nay")]);
        var withoutBreak = await RenderedAsync([Split('a', day, "Yea")]);

        var switchButtons = withBreak.QuerySelectorAll("[aria-labelledby='panel-records'] [data-layout-choice]");
        Assert.Equal(["All records", "Votes against their party"], switchButtons.Select(button => button.TextContent));
        Assert.Equal("all", withBreak.QuerySelector("[aria-labelledby='panel-records']")!.GetAttribute("data-layout"));
        Assert.Null(withoutBreak.QuerySelector("[aria-labelledby='panel-records'] [data-layout-choice]"));
        Assert.Null(withoutBreak.QuerySelector("[aria-labelledby='panel-records']")!.GetAttribute("data-layout"));
        Assert.NotNull(withBreak.QuerySelector("[aria-labelledby='panel-records'] [data-layout-switch][hidden]"));
        Assert.Equal(["true", "false"], switchButtons.Select(button => button.GetAttribute("aria-pressed")));
    }

    [Fact]
    public async Task AnEarlierCongressPageHasNoRecordsSwitchEvenWithAVoteAgainstTheirParty()
    {
        var page = new ReleaseOfficialPage(Profile(), true, 118, false, [], [], [Split('a', new DateOnly(2024, 4, 1), "Nay") with { Congress = 118 }]);

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        Assert.NotNull(document.QuerySelector("tbody tr.against-party"));
        Assert.Null(document.QuerySelector("[data-layout-choice]"));
    }

    private static async Task<AngleSharp.Dom.IDocument> RenderedAsync(PublishedOfficialVote[] votes)
    {
        var page = Current([], votes);
        page = page with { TalkAndAction = TalkAndActionProfile.Of(119, page.Records, page.Votes) };
        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);
        return new HtmlParser().ParseDocument(html);
    }

    private static PublishedOfficialVote Split(char id, DateOnly date, string cast) =>
        Vote(id, date) with { SplitVote = true, PartyMajority = PublishedPartyMajority.Yea, Cast = cast };

    [Fact]
    public async Task TheProfilePanelShowsCountsAndSharesOrTooFewRecordsAndLinksTheMethodNote()
    {
        PublishedRecordEntry[] statements = [.. Enumerable.Range(1, 5).Select(index => Entry((char)('0' + index), PublishedRecordKind.Statement, new DateOnly(2026, 3, 1)) with { IssueIds = index < 3 ? ["housing", "health"] : ["housing"] })];
        var page = Current(statements, [Vote('a', new DateOnly(2026, 4, 1)) with { IssueIds = ["housing"] }, Vote('b', new DateOnly(2026, 4, 2)) with { Kind = PublishedVoteKind.Cloture },
            Vote('c', new DateOnly(2026, 4, 3)) with { IssueIds = ["health"], Cast = PublishedOfficialVote.NotVoting },
            Vote('d', new DateOnly(2026, 4, 4)) with { Kind = PublishedVoteKind.Cloture }]);
        page = page with { TalkAndAction = TalkAndActionProfile.Of(119, page.Records, page.Votes) };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var panel = new HtmlParser().ParseDocument(html).QuerySelector("[aria-labelledby='panel-talk-and-action-in-congress-119']")!;
        Assert.Equal(["health", "2 (40%)", "0 (Too few records)", "0 (Too few records)"],
            panel.QuerySelectorAll("tbody tr:nth-child(1) td").Select(cell => cell.TextContent));
        Assert.Equal(["housing", "5 (100%)", "0 (Too few records)", "1 (Too few records)"],
            panel.QuerySelectorAll("tbody tr:nth-child(2) td").Select(cell => cell.TextContent));
        Assert.DoesNotContain("marked Not Voting", panel.TextContent, StringComparison.Ordinal);
        Assert.Contains("Procedural votes left out: 2", panel.TextContent, StringComparison.Ordinal);
        Assert.Equal("../method.html", panel.QuerySelector("a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task APageWithNoProfileHasNoProfilePanel()
    {
        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(Current([], []), PublicationCatalogs.None, CancellationToken.None);

        Assert.DoesNotContain("Talk and action", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCurrentPageLinksEachEarlierCongressPageFromItsOwnFolder()
    {
        var page = Current([], [Vote('3', new DateOnly(2026, 4, 1))]) with { PastCongresses = [118, 117] };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var links = new HtmlParser().ParseDocument(html).QuerySelectorAll("[aria-labelledby='panel-earlier-congresses'] a");
        Assert.Equal(["../officials/mayor/118.html", "../officials/mayor/117.html"], links.Select(link => link.GetAttribute("href")));
    }

    [Fact]
    public async Task AnEarlierCongressPageHoldsOnlyThatCongressVotesAndLinksBackAndResolvesItsAssetsFromTwoFoldersDeep()
    {
        var page = new ReleaseOfficialPage(Profile(), true, 118, false, [], [], [Vote('3', new DateOnly(2024, 4, 1)) with { Congress = 118 }]);

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        Assert.Equal(["../../index.html", "../../officials.html", "../../officials/mayor.html"],
            document.QuerySelectorAll("nav[aria-label='Breadcrumb'] a").Select(link => link.GetAttribute("href")));
        Assert.Equal("Mayor Example, Congress 118", document.QuerySelector("nav[aria-label='Breadcrumb'] [aria-current='page']")!.TextContent);
        Assert.Equal(["../../records/" + new string('3', 64) + ".html"], document.QuerySelectorAll("tbody a.cell-title").Select(link => link.GetAttribute("href")));
        Assert.Contains("href=\"../../assets/css/theme.css\"", html, StringComparison.Ordinal);
        Assert.Null(document.QuerySelector("[aria-labelledby='panel-offices']"));
        Assert.Contains("Congress 118", document.QuerySelector("title")!.TextContent, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(118, "Mayor Example, Representative for WA, district 1. Votes in Congress 118")]
    [InlineData(119, "Mayor Example. Votes in Congress 119")]
    [InlineData(110, "Mayor Example. Votes in Congress 110")]
    public async Task AnEarlierCongressDescriptionNamesTheOfficeHeldInThatCongressAndNoneWhenThereWasNone(int congress, string expectedStart)
    {
        var profile = Profile() with
        {
            Terms = [new() { Office = "rep", State = "WA", District = 1, Party = "Democrat", Start = new(2021, 1, 3), End = new(2025, 1, 3) }]
        };
        var page = new ReleaseOfficialPage(profile, true, congress, false, [], [], []);

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var description = new HtmlParser().ParseDocument(html).QuerySelector("meta[name='description']")!.GetAttribute("content")!;
        Assert.StartsWith(expectedStart, description, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOfficialWithNoTermStatesThatTheRostersAndTheConfigurationListNoOffice()
    {
        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(Current([Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1))], []),
            PublicationCatalogs.None, CancellationToken.None);

        Assert.Contains("The rosters and the configuration list no dated office for this official.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOfficialWhoLeftTheConfigurationSaysThatCivicLensNoLongerTracksThem()
    {
        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(Current([Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1))], []) with { IsTracked = false },
            PublicationCatalogs.None, CancellationToken.None);

        Assert.Contains("Civic Lens no longer tracks this official", html, StringComparison.Ordinal);
        Assert.DoesNotContain("The rosters and the configuration list no dated office", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheIssueColumnShowsTheConfiguredNameLinkedToTheIssuePageAndAnUnconfiguredIdAsPlainText()
    {
        PublishedRecordEntry[] entries = [Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1)) with { IssueIds = ["housing", "mystery"] }];
        var page = Current(entries, []);
        page = page with { TalkAndAction = TalkAndActionProfile.Of(119, page.Records, page.Votes) };
        var catalog = new PublicationCatalog(new Dictionary<string, string>(), new IssueConfiguration
        {
            PolicyAreas = [new() { Id = "land-use", Name = "Land use" }],
            Issues = [new() { Id = "housing", Name = "Housing costs", Definition = "d", PolicyAreaIds = ["land-use"] }]
        });

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, catalog, CancellationToken.None);

        var cells = new HtmlParser().ParseDocument(html)
            .QuerySelectorAll("[aria-labelledby='panel-talk-and-action-in-congress-119'] tbody tr td:first-child").ToList();
        Assert.Equal(["Housing costs", "mystery"], cells.Select(cell => cell.TextContent.Trim()));
        Assert.Equal("../" + PublicationProtocol.IssuePagePath("housing", 1), cells[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Null(cells[1].QuerySelector("a"));
    }

    [Fact]
    public async Task TheOfficialListLinksEachPageAndShowsTheCountOfRecordsOtherThanVotesThatNameTheOfficial()
    {
        var html = await new ReleaseRenderer(null, null).RenderOfficialsAsync([new OfficialListing(Profile(), 2)], CancellationToken.None);

        var row = new HtmlParser().ParseDocument(html).QuerySelector("tbody tr")!;
        Assert.Equal("officials/mayor.html", row.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("2", row.QuerySelector("td.num")!.TextContent);
        Assert.Equal("Records other than votes", new HtmlParser().ParseDocument(html).QuerySelector("thead th.num")!.TextContent);
    }

    [Fact]
    public async Task TheLinkedPairsPanelComesBeforeTheVotesWithTheirPartyPanelAndListsEachPairWithTheLinkWordsTheTitleAndTheCast()
    {
        var page = Current([], [Split('a', new DateOnly(2026, 4, 1), "Nay")]);
        page = page with
        {
            TalkAndAction = TalkAndActionProfile.Of(119, page.Records, page.Votes),
            Pairs = [Pair('2', 'a', PublishedRelationshipType.StatementAboutVote, PublishedRecordKind.Vote, new DateOnly(2026, 4, 5), "Nay"),
                Pair('1', 'b', PublishedRelationshipType.StatesPositionOn, PublishedRecordKind.Action, new DateOnly(2026, 3, 1), null)]
        };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        var panels = document.QuerySelectorAll("section[aria-labelledby]").Select(panel => panel.GetAttribute("aria-labelledby")).ToArray();
        Assert.True(Array.IndexOf(panels, "panel-what-they-said-and-what-they-did") + 1 == Array.IndexOf(panels, "panel-votes-with-their-party"));
        var panel = document.QuerySelector("[aria-labelledby='panel-what-they-said-and-what-they-did']")!;
        Assert.Equal(["Date", "What they said", "Link", "What they did", "Their vote"], panel.QuerySelectorAll("thead th").Select(cell => cell.TextContent.Trim()));
        var rows = panel.QuerySelectorAll("tbody tr").Select(row => row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()).ToArray()).ToArray();
        Assert.Equal(["2026-04-05", "Statement 2", "Statement about a vote", "Official title a", "Nay"], rows[0]);
        Assert.Equal(["2026-03-01", "Statement 1", "States position on", "Official title b", ""], rows[1]);
        Assert.Equal([$"../records/{new string('2', 64)}.html", $"../records/{new string('a', 64)}.html"],
            panel.QuerySelectorAll("tbody tr")[0].QuerySelectorAll("a").Select(link => link.GetAttribute("href")));
        Assert.Contains("A reviewer approved each link. A link does not mean the vote or bill matches the statement.", panel.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AVoteLinkWithNoCastRowOfTheOfficialSaysSoInsteadOfLeavingTheCellEmpty()
    {
        var page = Current([], []);
        page = page with
        {
            TalkAndAction = TalkAndActionProfile.Of(119, page.Records, page.Votes),
            Pairs = [Pair('1', 'a', PublishedRelationshipType.StatementAboutVote, PublishedRecordKind.Vote, new DateOnly(2026, 4, 5), null)]
        };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var cells = new HtmlParser().ParseDocument(html).QuerySelectorAll("[aria-labelledby='panel-what-they-said-and-what-they-did'] tbody td");
        Assert.Equal("No vote on record", cells[^1].TextContent.Trim());
    }

    [Fact]
    public async Task AnOfficialWithNoPairKeepsThePanelWithOneLineThatSaysThereIsNoLinkYet()
    {
        var page = Current([], []);
        page = page with { TalkAndAction = TalkAndActionProfile.Of(119, page.Records, page.Votes) };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var panel = new HtmlParser().ParseDocument(html).QuerySelector("[aria-labelledby='panel-what-they-said-and-what-they-did']")!;
        Assert.Contains("No reviewed links between their statements and their bills or votes yet.", panel.TextContent, StringComparison.Ordinal);
        Assert.Null(panel.QuerySelector("table"));
    }

    [Fact]
    public async Task AnEarlierCongressPageHasNoLinkedPairsPanel()
    {
        var page = new ReleaseOfficialPage(Profile(), true, 118, false, [], [], [Vote('3', new DateOnly(2024, 4, 1)) with { Congress = 118 }]);

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        Assert.DoesNotContain("What they said and what they did", html, StringComparison.Ordinal);
    }

    private static LinkedPair Pair(char statement, char target, PublishedRelationshipType type, PublishedRecordKind kind, DateOnly day, string? cast) =>
        new(day, new string(statement, 64), $"Statement {statement}", type, kind, new string(target, 64), $"Official title {target}", cast);

    private static PublishedOfficialProfile Profile() => new() { Id = "mayor", Name = "Mayor Example", Terms = [] };

    private static ReleaseOfficialPage Current(PublishedRecordEntry[] entries, PublishedOfficialVote[] votes) =>
        new(Profile(), true, 119, true, [], [.. entries], [.. votes]);

    private static PublishedRecordEntry Entry(char id, PublishedRecordKind kind, DateOnly date) => new()
    {
        IssueIds = [],
        PolicyAreaIds = [],
        Kind = kind,
        RecordId = new string(id, 64),
        RevisionNumber = 1,
        Headline = $"Entry {id}",
        FirstPublishedAtUtc = new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero),
        EventDate = date,
        Officials = [new() { Id = "mayor", Name = "Mayor Example" }]
    };

    private static PublishedOfficialVote Vote(char id, DateOnly date) => new()
    {
        RecordId = new string(id, 64),
        VotedOn = date,
        Chamber = PublishedChamber.House,
        Congress = 119,
        Session = 1,
        RollCallNumber = 100,
        BillType = "hr",
        BillNumber = 7,
        Cast = "Yea",
        Result = "Passed",
        Totals = new() { Yea = 218, Nay = 210, Present = 1, NotVoting = 5 },
        SplitVote = false,
        Party = "Republican",
        Headline = $"Vote {id}",
        Kind = PublishedVoteKind.Passage,
        IssueIds = []
    };
}
