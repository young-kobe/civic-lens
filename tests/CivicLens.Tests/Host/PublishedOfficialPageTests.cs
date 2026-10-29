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
                    new() { Office = "rep", Kind = PublishedOfficeKind.Representative, State = "WA", District = 1, Party = "Democrat", Start = new(1993, 1, 3), End = new(2025, 1, 3) },
                    new() { Office = "Secretary of State", Kind = PublishedOfficeKind.Executive, Start = new(2025, 1, 21) }
                ]
            }
        };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        Assert.Equal("Mayor Example", document.QuerySelector("h1")!.TextContent);
        Assert.Equal(["Representative for WA, district 1", "Secretary of State"],
            document.QuerySelectorAll("[aria-labelledby='panel-offices'] tbody tr td:first-child").Select(cell => cell.TextContent));
        Assert.Equal(["2025-01-03", "Present"], document.QuerySelectorAll("[aria-labelledby='panel-offices'] tbody tr td[data-label='To']").Select(cell => cell.TextContent));
        Assert.All(document.QuerySelectorAll("table"), table => Assert.NotNull(table.QuerySelector("caption")));
        Assert.Contains("href=\"../officials.html\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"../assets/css/theme.css\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOfficialWithNoPartyOnAnyTermGetsNoPartyColumnBecauseItWouldBeEmpty()
    {
        var page = Current([], []) with { Profile = Profile("pres", "Pat President", new PublishedOfficeTerm { Office = "President", Kind = PublishedOfficeKind.Executive, Start = new DateOnly(2025, 1, 20) }) };

        var offices = await PanelTableAsync(page, "panel-offices");

        Assert.Equal(["Office", "From", "To"], offices.QuerySelectorAll("thead th").Select(cell => cell.TextContent));
        Assert.Equal(3, offices.QuerySelectorAll("tbody td").Length);
    }

    [Fact]
    public async Task AnOfficialWithAPartyOnOneTermKeepsThePartyColumnForEveryTerm()
    {
        var page = Current([], []) with
        {
            Profile = Profile("a", "Ann Able", Term("sen", "MA", "Democratic"), new PublishedOfficeTerm { Office = "President", Kind = PublishedOfficeKind.Executive, Start = new DateOnly(2030, 1, 20) })
        };

        var offices = await PanelTableAsync(page, "panel-offices");

        Assert.Equal(["Office", "Party", "From", "To"], offices.QuerySelectorAll("thead th").Select(cell => cell.TextContent));
        Assert.All(offices.QuerySelectorAll("tbody tr"), row => Assert.Equal(4, row.QuerySelectorAll("td").Length));
    }

    [Fact]
    public async Task APageWithNoCastOnAnyRowGetsNoCastColumnBecauseOnlyAVoteHasACast()
    {
        var records = await PanelTableAsync(Current([Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1))], []), "panel-records");

        Assert.Equal(["Date", "Record"], records.QuerySelectorAll("thead th").Select(cell => cell.TextContent));
        Assert.Equal(2, records.QuerySelectorAll("tbody td").Length);
    }

    [Fact]
    public async Task APageWithOneVoteKeepsTheCastColumnForEveryRow()
    {
        var records = await PanelTableAsync(Current([Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1))], [Vote('3', new DateOnly(2026, 4, 1))]), "panel-records");

        Assert.Equal(["Date", "Record", "Cast"], records.QuerySelectorAll("thead th").Select(cell => cell.TextContent));
        Assert.All(records.QuerySelectorAll("tbody tr"), row => Assert.Equal(3, row.QuerySelectorAll("td").Length));
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
    public async Task TheCurrentPageShowsThePortraitFromTheReleaseRootWithItsSizeAltTextAndCredit()
    {
        var hash = new string('c', 64);
        var photo = new PublishedPhoto
        {
            Path = PublicationProtocol.PhotoPath(hash),
            Credit = "Official portrait. Public domain.",
            SourceUrl = "https://example.gov/images/mayor.jpg",
            Width = 225,
            Height = 275
        };
        var page = Current([], []) with { Profile = Profile() with { Photo = photo } };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var figure = new HtmlParser().ParseDocument(html).QuerySelector("figure.portrait")!;
        var image = figure.QuerySelector("img")!;
        Assert.Equal($"../photos/{hash}.jpg", image.GetAttribute("src"));
        Assert.Equal(("225", "275", "eager"), (image.GetAttribute("width"), image.GetAttribute("height"), image.GetAttribute("loading")));
        Assert.Equal("Official portrait of Mayor Example", image.GetAttribute("alt"));
        Assert.Equal("Official portrait. Public domain.", figure.QuerySelector("figcaption")!.TextContent);
        Assert.Null(figure.QuerySelector("svg"));
    }

    [Fact]
    public async Task AnOfficialWithNoPhotoShowsAHiddenPlaceholderInTheSameFrameAndSaysSo()
    {
        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(Current([], []), PublicationCatalogs.None, CancellationToken.None);

        var figure = new HtmlParser().ParseDocument(html).QuerySelector("figure.portrait")!;
        Assert.Null(figure.QuerySelector("img"));
        var placeholder = figure.QuerySelector("svg")!;
        Assert.Equal(("true", "0 0 225 275"), (placeholder.GetAttribute("aria-hidden"), placeholder.GetAttribute("viewBox")));
        Assert.Equal("No portrait is shown for this official.", figure.QuerySelector("figcaption")!.TextContent);
    }

    [Fact]
    public async Task APastCongressPageShowsNoPortraitBecauseOnlyTheCurrentPageHasOne()
    {
        var past = new ReleaseOfficialPage(Profile(), true, 118, false, [], [], []);

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(past, PublicationCatalogs.None, CancellationToken.None);

        Assert.Null(new HtmlParser().ParseDocument(html).QuerySelector("figure.portrait"));
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

        var paragraphs = new HtmlParser().ParseDocument(html).QuerySelectorAll("[aria-labelledby='panel-missed-votes-in-congress-119'] p")
            .Select(paragraph => paragraph.TextContent.Trim()).ToArray();
        Assert.Equal(["Missed 1 of 2 roll call votes of all kinds, including procedural votes, held in the House of Representatives while they served, 1 Apr 2026 to 3 Apr 2026 (119th Congress).",
            "Missed 0 of 1 roll call vote of all kinds, including procedural votes, held in the Senate while they served, on 2 Apr 2026 (119th Congress).",
            "A missed vote means the official did not vote on it."], paragraphs[..3]);
        Assert.Equal(["muted small"], new HtmlParser().ParseDocument(html).QuerySelectorAll("[aria-labelledby='panel-missed-votes-in-congress-119'] p.muted").Select(paragraph => paragraph.ClassName!));
    }

    [Fact]
    public async Task TheMissedVotesSentenceShowsTheShareOnlyAtTheThresholdAndAnOfficialWithNoRowsOfAChamberGetsNoSentenceForIt()
    {
        var votes = Enumerable.Range(0, 5).Select(index => Vote((char)('a' + index), new DateOnly(2026, 4, 1)) with
        { Cast = index == 0 ? PublishedOfficialVote.NotVoting : "Yea" }).ToArray();
        var page = Current([], votes);
        page = page with { TalkAndAction = TalkAndActionProfile.Of(119, page.Records, page.Votes) };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var text = new HtmlParser().ParseDocument(html).QuerySelector("[aria-labelledby='panel-missed-votes-in-congress-119']")!.TextContent;
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

        var text = new HtmlParser().ParseDocument(html).QuerySelector("[aria-labelledby='panel-missed-votes-in-congress-119']")!.TextContent;
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
        Assert.True(Array.IndexOf(titles, "Missed votes in Congress 119") < Array.IndexOf(titles, "Votes with their party"));
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
    public async Task TheIssuePanelShowsPlainCountsAndTheTitlesOfTheSponsoredBillsAndLeavesOutProceduralVotes()
    {
        PublishedRecordEntry[] entries = [.. Enumerable.Range(1, 3).Select(index => Entry((char)('0' + index), PublishedRecordKind.Statement, new DateOnly(2026, 3, 1)) with { IssueIds = index < 3 ? ["housing", "health"] : ["housing"] }),
            Entry('a', PublishedRecordKind.Action, new DateOnly(2026, 5, 1)) with { IssueIds = ["housing"], BillTitle = "Home Act" }];
        var page = Current(entries, [Vote('a', new DateOnly(2026, 4, 1)) with { IssueIds = ["housing"] }, Vote('b', new DateOnly(2026, 4, 2)) with { Kind = PublishedVoteKind.Cloture, IssueIds = ["housing"] },
            Vote('c', new DateOnly(2026, 4, 3)) with { IssueIds = ["health"], Cast = PublishedOfficialVote.NotVoting }]);
        page = page with { TalkAndAction = TalkAndActionProfile.Of(119, page.Records, page.Votes) };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var panel = new HtmlParser().ParseDocument(html).QuerySelector("[aria-labelledby='panel-issues-they-work-on']")!;
        Assert.Equal(["Issue", "Their statements", "Bills they sponsored", "Votes they cast"], panel.QuerySelectorAll("th").Select(cell => cell.TextContent.Trim()));
        Assert.Equal(["health", "2", "0", "0"], panel.QuerySelectorAll("tbody tr:nth-child(1) td").Select(cell => cell.TextContent.Trim()));
        var housing = panel.QuerySelectorAll("tbody tr:nth-child(2) td").ToArray();
        Assert.Equal(["housing", "3", "1", "1"], new[] { housing[0], housing[1], housing[2].FirstChild!, housing[3] }.Select(cell => cell.TextContent.Trim()));
        Assert.Equal(["Home Act"], housing[2].QuerySelectorAll("li").Select(item => item.TextContent));
        Assert.Equal("../" + PublicationProtocol.RecordPagePath(new string('a', 64)), housing[2].QuerySelector("a")!.GetAttribute("href"));
        Assert.DoesNotContain("%", panel.TextContent, StringComparison.Ordinal);
        Assert.Equal("../method.html", panel.QuerySelector("p a")!.GetAttribute("href"));
        Assert.Contains("\"Votes they cast\" counts only substantive votes. A statement, bill, or vote on several issues counts once for each issue.", panel.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOfficialWithNoRecordOnATrackedIssueGetsAPlainSentenceInPlaceOfTheIssueTable()
    {
        var page = Current([], [Vote('a', new DateOnly(2026, 4, 1))]);
        page = page with { TalkAndAction = TalkAndActionProfile.Of(119, page.Records, page.Votes) };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var panel = new HtmlParser().ParseDocument(html).QuerySelector("[aria-labelledby='panel-issues-they-work-on']")!;
        Assert.Null(panel.QuerySelector("table"));
        Assert.Contains("No tracked issue", panel.TextContent, StringComparison.Ordinal);
        Assert.Contains("None of their statements, sponsored bills, or votes in this Congress is on a tracked issue.", panel.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task APageWithNoProfileHasNoProfilePanel()
    {
        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(Current([], []), PublicationCatalogs.None, CancellationToken.None);

        Assert.DoesNotContain("Issues they work on", html, StringComparison.Ordinal);
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
            Terms = [new() { Office = "rep", Kind = PublishedOfficeKind.Representative, State = "WA", District = 1, Party = "Democrat", Start = new(2021, 1, 3), End = new(2025, 1, 3) }]
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
            .QuerySelectorAll("[aria-labelledby='panel-issues-they-work-on'] tbody tr td:first-child").ToList();
        Assert.Equal(["Housing costs", "mystery"], cells.Select(cell => cell.TextContent.Trim()));
        Assert.Equal("../" + PublicationProtocol.IssuePagePath("housing", 1), cells[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Null(cells[1].QuerySelector("a"));
    }

    [Fact]
    public async Task TheOfficialListShowsOneCardForEveryOfficialWithTheLinkOfficeAndPartyAndAFullSentenceCount()
    {
        var senator = Profile("warren", "Elizabeth Warren", Term("sen", "MA", "Democratic"));
        var quiet = Profile("quiet", "Quiet Member", Term("rep", "TX", "Republican") with { District = 3 });

        var cards = (await ListAsync(new OfficialListing(senator, 12), new OfficialListing(quiet, 0), new OfficialListing(Profile("one", "One Record"), 1)))
            .QuerySelectorAll("ul.officials > li").ToList();

        Assert.Equal(3, cards.Count);
        Assert.Equal("officials/warren.html", cards[0].QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal(["Senator for MA", "Party: Democratic", "12 records other than votes"], cards[0].QuerySelectorAll(".cell-sub").Select(line => line.TextContent));
        Assert.Contains("0 records other than votes", cards[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("Representative for TX, district 3", cards[1].TextContent, StringComparison.Ordinal);
        Assert.Contains("1 record other than votes", cards[2].TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnExecutiveCardShowsTheOfficeWithNoStateBecauseAnExecutiveHasNone()
    {
        var executive = Profile("pres", "Pat President", Term("prez", null, "Independent"));

        var card = (await ListAsync(new OfficialListing(executive, 4))).QuerySelector("ul.officials > li")!;

        Assert.Equal("President", card.QuerySelector(".cell-sub")!.TextContent);
        Assert.Null(card.GetAttribute("data-state"));
        Assert.Equal("executive", card.GetAttribute("data-office"));
    }

    [Fact]
    public async Task TheCardsCarryTheDataTheBrowserFiltersOnAndTheTabsSayWhoAndHowMany()
    {
        var page = await ListAsync(
            new OfficialListing(Profile("a", "Ann Able", Term("sen", "MA", "Democratic")), 1),
            new OfficialListing(Profile("b", "Bob Baker", Term("sen", "TX", "Republican")), 1),
            new OfficialListing(Profile("c", "Cy Cole", Term("rep", "MA", "Democratic")), 1));

        var cards = page.QuerySelectorAll("ul.officials > li").ToList();
        Assert.Equal([("Ann Able", "senator", "MA"), ("Bob Baker", "senator", "TX"), ("Cy Cole", "representative", "MA")],
            cards.Select(card => (card.GetAttribute("data-name")!, card.GetAttribute("data-office")!, card.GetAttribute("data-state")!)));
        Assert.Equal(["All officials (3)", "Senators (2)", "Representatives (1)"],
            page.QuerySelectorAll("[data-officials-filters] button").Select(button => button.TextContent.Trim()));
        Assert.Equal(["All states", "MA", "TX"], page.QuerySelectorAll("[data-officials-state] option").Select(option => option.TextContent));
        Assert.Equal(["true", "false", "false"], page.QuerySelectorAll("[data-officials-filters] button").Select(button => button.GetAttribute("aria-pressed")));
    }

    [Fact]
    public async Task AJudicialOfficeIsInTheJusticesTabAfterExecutivesAndAnExecutiveOfficeIsInExecutivesNotOtherOfficials()
    {
        var page = await ListAsync(
            new OfficialListing(Profile("a", "Ann Able", Term("sen", "MA", "Democratic")), 1),
            new OfficialListing(Profile("b", "Bob Baker", new PublishedOfficeTerm { Office = "Chief Justice", Kind = PublishedOfficeKind.Judicial, Start = new DateOnly(2005, 9, 29) }), 1),
            new OfficialListing(Profile("c", "Cy Cole", new PublishedOfficeTerm { Office = "Secretary of State", Kind = PublishedOfficeKind.Executive, Start = new DateOnly(2025, 1, 21) }), 1));

        Assert.Equal(["All officials (3)", "Senators (1)", "Executives (1)", "Justices (1)"],
            page.QuerySelectorAll("[data-officials-filters] button").Select(button => button.TextContent.Trim()));
        Assert.Equal(["senator", "judicial", "executive"], page.QuerySelectorAll("ul.officials > li").Select(card => card.GetAttribute("data-office")));
    }

    [Fact]
    public async Task TheFilterControlsAreHiddenInTheStaticPageSoAReaderWithoutScriptSeesEveryCardAndNoDeadControl()
    {
        var page = await ListAsync(new OfficialListing(Profile("a", "Ann Able", Term("sen", "MA", "Democratic")), 1));

        Assert.True(page.QuerySelector("[data-officials-filters]")!.HasAttribute("hidden"));
        Assert.False(page.QuerySelector("ul.officials > li")!.HasAttribute("hidden"));
        Assert.Equal("status", page.QuerySelector("[data-officials-status]")!.GetAttribute("role"));
    }

    [Fact]
    public async Task TheLatestTermByStartDecidesTheOfficeAndKindSoASenatorWhoWasARepresentativeIsASenator()
    {
        var profile = Profile("a", "Ann Able",
            Term("sen", "MA", "Democratic") with { Start = new DateOnly(2021, 1, 3) },
            Term("rep", "MA", "Democratic") with { Start = new DateOnly(2015, 1, 3) });

        var card = (await ListAsync(new OfficialListing(profile, 1))).QuerySelector("ul.officials > li")!;

        Assert.Contains("Senator for MA", card.TextContent, StringComparison.Ordinal);
        Assert.Equal("senator", card.GetAttribute("data-office"));
    }

    [Fact]
    public async Task AnEndedLatestTermReadsFormerButStillGroupsUnderItsOfficeSoTheReaderKnowsTheOfficialLeft()
    {
        var term = Term("rep", "MA", "Democratic") with { District = 4, End = new DateOnly(2025, 1, 3) };

        var card = (await ListAsync(new OfficialListing(Profile("a", "Ann Able", term), 1))).QuerySelector("ul.officials > li")!;

        Assert.Equal("Former Representative for MA, district 4", card.QuerySelector(".cell-sub")!.TextContent);
        Assert.Equal("representative", card.GetAttribute("data-office"));
    }

    [Fact]
    public async Task ATermEndsByTheReleaseDateNotTheRenderClockSoAListPublishedBeforeTheEndShowsTheOfficeAsCurrent()
    {
        var term = Term("rep", "MA", "Democratic") with { District = 4, End = new DateOnly(2025, 1, 3) };

        var page = await ListAsync(new DateTimeOffset(2025, 1, 2, 23, 0, 0, TimeSpan.Zero), new OfficialListing(Profile("a", "Ann Able", term), 1));

        Assert.Equal("Representative for MA, district 4", page.QuerySelector("ul.officials > li .cell-sub")!.TextContent);
    }

    [Fact]
    public async Task ACaucusThatDiffersFromThePartyIsNamedSoAnIndependentShowsWhoseCaucusTheyJoin()
    {
        var independent = Term("sen", "VT", "Independent") with { Caucus = "Democrat" };
        var same = Term("sen", "TX", "Republican") with { Caucus = "Republican" };

        var cards = (await ListAsync(new OfficialListing(Profile("a", "Ann Able", independent), 1), new OfficialListing(Profile("b", "Bob Baker", same), 1)))
            .QuerySelectorAll("ul.officials > li").ToList();

        Assert.Equal("Party: Independent, caucuses with Democrats", cards[0].QuerySelectorAll(".cell-sub")[1].TextContent);
        Assert.Equal("Party: Republican", cards[1].QuerySelectorAll(".cell-sub")[1].TextContent);
    }

    [Fact]
    public async Task AnOfficialWithNoTermIsInTheOtherOfficialsTabAndAllOfficialsOnly()
    {
        var page = await ListAsync(new OfficialListing(Profile("a", "Ann Able", Term("sen", "MA", "Democratic")), 1), new OfficialListing(Profile("b", "No Term"), 1));

        Assert.Equal("other", page.QuerySelectorAll("ul.officials > li")[1].GetAttribute("data-office"));
        Assert.Equal(["All officials (2)", "Senators (1)", "Other officials (1)"],
            page.QuerySelectorAll("[data-officials-filters] button").Select(button => button.TextContent.Trim()));
    }

    [Fact]
    public async Task TheCardListKeepsItsListRoleBecauseListStyleNoneDropsItInSafari()
    {
        var page = await ListAsync(new OfficialListing(Profile("a", "Ann Able"), 1));

        Assert.Equal("list", page.QuerySelector("ul.officials")!.GetAttribute("role"));
    }

    [Fact]
    public async Task CardPortraitsLoadLazilyAndACardWithNoPhotoShowsThePlaceholder()
    {
        var page = await ListAsync(new OfficialListing(WithPhoto("a", "Ann Able", "Public domain."), 1), new OfficialListing(Profile("b", "Bob Baker"), 1));

        var cards = page.QuerySelectorAll("ul.officials > li").ToList();
        var image = cards[0].QuerySelector("img")!;
        Assert.Equal(("lazy", "225", "275"), (image.GetAttribute("loading"), image.GetAttribute("width"), image.GetAttribute("height")));
        Assert.Null(cards[1].QuerySelector("img"));
        Assert.NotNull(cards[1].QuerySelector("svg.portrait-placeholder"));
        Assert.Null(cards[1].QuerySelector("figcaption"));
    }

    [Fact]
    public async Task TheCreditShowsOnceBelowTheListWhenEveryPhotoHasTheSameCredit()
    {
        var page = await ListAsync(new OfficialListing(WithPhoto("a", "Ann Able", "Public domain."), 1), new OfficialListing(WithPhoto("b", "Bob Baker", "Public domain."), 1));

        Assert.Empty(page.QuerySelectorAll("ul.officials figcaption"));
        Assert.Single(page.QuerySelectorAll(".caveat"));
        Assert.Contains("Public domain.", page.QuerySelector(".caveat")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheCreditShowsOnEachCardWhenCreditsDifferSoNoLicenseTextIsHidden()
    {
        var page = await ListAsync(new OfficialListing(WithPhoto("a", "Ann Able", "Public domain."), 1), new OfficialListing(WithPhoto("b", "Bob Baker", "Used with permission."), 1));

        Assert.Equal(["Public domain.", "Used with permission."], page.QuerySelectorAll("ul.officials figcaption").Select(caption => caption.TextContent));
        Assert.Empty(page.QuerySelectorAll(".caveat"));
    }

    [Fact]
    public async Task TheProfilePortraitStaysEagerBecauseItIsTheFirstThingOnThePage()
    {
        var page = Current([], []) with { Profile = WithPhoto("mayor", "Mayor Example", "Public domain.") };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        Assert.Equal("eager", new HtmlParser().ParseDocument(html).QuerySelector("figure.portrait img")!.GetAttribute("loading"));
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
        Assert.Equal(["Statement date", "What they said", "Link", "What they did", "Their vote"], panel.QuerySelectorAll("thead th").Select(cell => cell.TextContent.Trim()));
        var rows = panel.QuerySelectorAll("tbody tr").Select(row => row.QuerySelectorAll("td").Select(cell => cell.TextContent.Trim()).ToArray()).ToArray();
        Assert.Equal(["2026-04-05", "Statement 2", "Statement about a vote", "Official title a", "Nay"], rows[0]);
        Assert.Equal(["2026-03-01", "Statement 1", "States position on", "Official title b", "Not a vote"], rows[1]);
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
    public async Task AnUndatedStatementShowsNoDateInsteadOfAnInventedDay()
    {
        var page = Current([], []);
        page = page with
        {
            TalkAndAction = TalkAndActionProfile.Of(119, page.Records, page.Votes),
            Pairs = [Pair('1', 'b', PublishedRelationshipType.StatesPositionOn, PublishedRecordKind.Action, null, null)]
        };

        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);

        var cells = new HtmlParser().ParseDocument(html).QuerySelectorAll("[aria-labelledby='panel-what-they-said-and-what-they-did'] tbody td");
        Assert.Equal("No date", cells[0].TextContent.Trim());
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

    private static LinkedPair Pair(char statement, char target, PublishedRelationshipType type, PublishedRecordKind kind, DateOnly? day, string? cast) =>
        new(day, new string(statement, 64), $"Statement {statement}", type, kind, new string(target, 64), $"Official title {target}", cast);

    [Fact]
    public async Task EachCountButtonNamesTheRowsItFiltersAndTheNumberShownEqualsTheCountClicked()
    {
        var document = await RenderedWithCountsAsync();

        var counts = document.QuerySelectorAll("[aria-labelledby='panel-issues-they-work-on'] [data-count-filter]").ToArray();
        Assert.Equal(["statement:health", "statement:housing", "bill:housing", "vote:housing"], counts.Select(count => count.GetAttribute("data-count-filter")));
        foreach (var count in counts)
        {
            var rows = document.QuerySelectorAll("[data-records] tbody tr").Where(row => row.GetAttribute("data-filter-keys")!.Split(' ').Contains(count.GetAttribute("data-count-filter"))).ToArray();
            Assert.Equal(int.Parse(count.TextContent), rows.Length);
            Assert.EndsWith($": {rows.Length} {(rows.Length == 1 ? "record" : "records")}", count.GetAttribute("data-filter-shows"), StringComparison.Ordinal);
        }
    }

    [Fact]
    public async Task OnlyARowInTheCurrentCongressCarriesAStatementOrBillMarkerAndOnlyASubstantiveCastVoteCarriesAVoteMarker()
    {
        var document = await RenderedWithCountsAsync();

        var marked = document.QuerySelectorAll("[data-records] tbody tr").ToDictionary(row => row.QuerySelector("a.cell-title")!.TextContent, row => row.GetAttribute("data-filter-keys"));
        Assert.Equal("statement:health statement:housing", marked["Entry 1"]);
        Assert.Equal("", marked["Entry 5"]);
        Assert.Equal("", marked["Entry 6"]);
        Assert.Equal("bill:housing", marked["Entry d"]);
        Assert.Equal("vote:housing", marked["Vote a"]);
        Assert.Equal("", marked["Vote b"]);
        Assert.Equal("", marked["Vote c"]);
    }

    [Fact]
    public async Task TheServerHtmlHasPlainCountsWithAZeroNeverAButtonAndEveryRowVisible()
    {
        var document = await RenderedWithCountsAsync();

        var health = document.QuerySelectorAll("[aria-labelledby='panel-issues-they-work-on'] tbody tr:nth-child(1) td").Select(cell => cell.TextContent.Trim()).ToArray();
        Assert.Equal(["health", "1", "0", "0"], health);
        Assert.Equal(4, document.QuerySelectorAll("[data-count-filter]").Length);
        Assert.Empty(document.QuerySelectorAll("[data-count-filter] button"));
        Assert.Empty(document.QuerySelectorAll("[data-records] tbody tr[hidden]"));
        Assert.Equal(9, document.QuerySelectorAll("[data-records] tbody tr").Length);
    }

    [Fact]
    public async Task TheFilterNoticeStatesWhatIsShownInPlainWordsAndOffersToShowAllRecords()
    {
        var document = await RenderedWithCountsAsync();

        var notice = document.QuerySelector("[data-records] [data-filter-notice][hidden]")!;
        Assert.Equal("Show all records", notice.QuerySelector("button[data-filter-clear]")!.TextContent);
        var counts = document.QuerySelectorAll("[data-count-filter]").ToArray();
        Assert.Equal(["Showing their statements on health in the 119th Congress: 1 record",
            "Showing their statements on housing in the 119th Congress: 3 records",
            "Showing bills they sponsored on housing in the 119th Congress: 1 record",
            "Showing their substantive votes on housing in the 119th Congress: 1 record"], counts.Select(count => count.GetAttribute("data-filter-shows")));
        Assert.Equal("1: show bills they sponsored on housing", counts[2].GetAttribute("data-filter-label"));
        Assert.Equal("", document.QuerySelector("[data-records] [data-filter-status][role='status']")!.TextContent);
        Assert.False(document.QuerySelector("[data-filter-status]")!.HasAttribute("hidden"));
    }

    [Fact]
    public async Task APageWithNoProfileHasNoRecordsFilterMarkup()
    {
        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(Current([], []), PublicationCatalogs.None, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        Assert.Null(document.QuerySelector("[data-records]"));
        Assert.Null(document.QuerySelector("[data-filter-notice]"));
        Assert.Null(document.QuerySelector("tbody tr[data-filter-keys]"));
    }

    private static async Task<AngleSharp.Dom.IDocument> RenderedWithCountsAsync()
    {
        PublishedRecordEntry[] entries =
        [.. Enumerable.Range(1, 3).Select(index => Entry((char)('0' + index), PublishedRecordKind.Statement, new DateOnly(2026, 3, 1)) with { IssueIds = index == 1 ? ["housing", "health"] : ["housing"] }),
            Entry('5', PublishedRecordKind.Statement, new DateOnly(2024, 3, 1)) with { IssueIds = ["housing"] },
            Entry('6', PublishedRecordKind.Action, new DateOnly(2024, 3, 1)) with { IssueIds = ["housing"] },
            Entry('d', PublishedRecordKind.Action, new DateOnly(2026, 5, 1)) with { IssueIds = ["housing"], BillTitle = "Home Act" }];
        var page = Current(entries, [Vote('a', new DateOnly(2026, 4, 1)) with { IssueIds = ["housing"] },
            Vote('b', new DateOnly(2026, 4, 2)) with { Kind = PublishedVoteKind.Cloture, IssueIds = ["housing"] },
            Vote('c', new DateOnly(2026, 4, 3)) with { IssueIds = ["housing"], Cast = PublishedOfficialVote.NotVoting }]);
        page = page with { TalkAndAction = TalkAndActionProfile.Of(119, page.Records, page.Votes) };
        var html = await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None);
        return new HtmlParser().ParseDocument(html);
    }

    private static PublishedOfficialProfile Profile() => new() { Id = "mayor", Name = "Mayor Example", Terms = [] };

    private static PublishedOfficialProfile Profile(string id, string name, params PublishedOfficeTerm[] terms) => new() { Id = id, Name = name, Terms = terms };

    private static PublishedOfficeTerm Term(string office, string? state, string party) =>
        new() { Office = office, Kind = KindOf(office), State = state, Party = party, Start = new DateOnly(2025, 1, 3) };

    private static PublishedOfficeKind KindOf(string office) => office switch
    {
        "sen" => PublishedOfficeKind.Senator,
        "rep" => PublishedOfficeKind.Representative,
        "prez" => PublishedOfficeKind.Executive,
        _ => throw new ArgumentOutOfRangeException(nameof(office))
    };

    private static PublishedOfficialProfile WithPhoto(string id, string name, string credit) => Profile(id, name) with
    {
        Photo = new PublishedPhoto
        {
            Path = PublicationProtocol.PhotoPath(new string('c', 64)),
            Credit = credit,
            SourceUrl = "https://example.gov/a.jpg",
            Width = 225,
            Height = 275
        }
    };

    private static readonly DateTimeOffset ListPublishedAtUtc = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);

    private static Task<AngleSharp.Dom.IDocument> ListAsync(params OfficialListing[] officials) => ListAsync(ListPublishedAtUtc, officials);

    private static async Task<AngleSharp.Dom.IDocument> ListAsync(DateTimeOffset publishedAtUtc, params OfficialListing[] officials) =>
        new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderOfficialsAsync(officials, publishedAtUtc, CancellationToken.None));

    private static async Task<AngleSharp.Dom.IElement> PanelTableAsync(ReleaseOfficialPage page, string panelId) =>
        new HtmlParser().ParseDocument(await new ReleaseRenderer(null, null).RenderOfficialAsync(page, PublicationCatalogs.None, CancellationToken.None))
            .QuerySelector($"[aria-labelledby='{panelId}'] table")!;

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
