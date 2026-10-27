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
        Assert.Contains("House roll call 100, Congress 119, session 1, HR 7", records.TextContent, StringComparison.Ordinal);
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
        Assert.Contains("Substantive roll calls marked Not Voting: 1", panel.TextContent, StringComparison.Ordinal);
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
        Headline = $"Vote {id}",
        Kind = PublishedVoteKind.Passage,
        IssueIds = []
    };
}
