using AngleSharp.Html.Parser;
using CivicLens.Application.Publication;
using CivicLens.Host.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Host;

public sealed class PublishedOfficialPageTests
{
    [Fact]
    public async Task TheCurrentPageListsStatementsActionsAndVotesNewestFirstWithTheirOwnDates()
    {
        var page = Current(
            [Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1)), Entry('2', PublishedRecordKind.Action, new DateOnly(2026, 5, 1))],
            [Vote('3', new DateOnly(2026, 4, 1)), Vote('4', new DateOnly(2025, 12, 1))]);

        var html = await new ReleaseRenderer().RenderOfficialAsync(page, CancellationToken.None);

        var records = new HtmlParser().ParseDocument(html).QuerySelector("[aria-labelledby='panel-records']")!;
        Assert.Equal([new string('2', 64), new string('3', 64), new string('1', 64), new string('4', 64)],
            records.QuerySelectorAll("a.cell-title").Select(link => link.GetAttribute("href")!.Replace("../records/", "").Replace(".html", "")));
        Assert.Equal(["2026-05-01", "2026-04-01", "2026-03-01", "2025-12-01"], records.QuerySelectorAll("tbody td.num").Select(cell => cell.TextContent));
        Assert.Contains("Roll call 100, Congress 119, session 1, HR 7", records.TextContent, StringComparison.Ordinal);
        Assert.Contains("Yea", records.TextContent, StringComparison.Ordinal);
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

        var html = await new ReleaseRenderer().RenderOfficialAsync(page, CancellationToken.None);

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
    public async Task TheCurrentPageLinksEachEarlierCongressPageFromItsOwnFolder()
    {
        var page = Current([], [Vote('3', new DateOnly(2026, 4, 1))]) with { PastCongresses = [118, 117] };

        var html = await new ReleaseRenderer().RenderOfficialAsync(page, CancellationToken.None);

        var links = new HtmlParser().ParseDocument(html).QuerySelectorAll("[aria-labelledby='panel-earlier-congresses'] a");
        Assert.Equal(["../officials/mayor/118.html", "../officials/mayor/117.html"], links.Select(link => link.GetAttribute("href")));
    }

    [Fact]
    public async Task AnEarlierCongressPageHoldsOnlyThatCongressVotesAndLinksBackAndResolvesItsAssetsFromTwoFoldersDeep()
    {
        var page = new ReleaseOfficialPage(Profile(), true, 118, false, [], [], [Vote('3', new DateOnly(2024, 4, 1)) with { Congress = 118 }]);

        var html = await new ReleaseRenderer().RenderOfficialAsync(page, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        Assert.Equal("../../officials/mayor.html", document.QuerySelector("a.back-link")!.GetAttribute("href"));
        Assert.Equal(["../../records/" + new string('3', 64) + ".html"], document.QuerySelectorAll("tbody a.cell-title").Select(link => link.GetAttribute("href")));
        Assert.Contains("href=\"../../assets/css/theme.css\"", html, StringComparison.Ordinal);
        Assert.Null(document.QuerySelector("[aria-labelledby='panel-offices']"));
        Assert.Contains("Congress 118", document.QuerySelector("title")!.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOfficialWithNoTermStatesThatTheRostersAndTheConfigurationListNoOffice()
    {
        var html = await new ReleaseRenderer().RenderOfficialAsync(Current([Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1))], []),
            CancellationToken.None);

        Assert.Contains("The rosters and the configuration list no dated office for this official.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOfficialWhoLeftTheConfigurationSaysThatCivicLensNoLongerTracksThem()
    {
        var html = await new ReleaseRenderer().RenderOfficialAsync(Current([Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1))], []) with { IsTracked = false },
            CancellationToken.None);

        Assert.Contains("Civic Lens no longer tracks this official", html, StringComparison.Ordinal);
        Assert.DoesNotContain("The rosters and the configuration list no dated office", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheOfficialListLinksEachPageAndShowsTheCountOfRecordsOtherThanVotesThatNameTheOfficial()
    {
        var html = await new ReleaseRenderer().RenderOfficialsAsync([new OfficialListing(Profile(), 2)], CancellationToken.None);

        var row = new HtmlParser().ParseDocument(html).QuerySelector("tbody tr")!;
        Assert.Equal("officials/mayor.html", row.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("2", row.QuerySelector("td.num")!.TextContent);
        Assert.Equal("Records other than votes", new HtmlParser().ParseDocument(html).QuerySelector("thead th.num")!.TextContent);
    }

    private static PublishedOfficialProfile Profile() => new() { Id = "mayor", Name = "Mayor Example" };

    private static ReleaseOfficialPage Current(PublishedRecordEntry[] entries, PublishedOfficialVote[] votes) =>
        new(Profile(), true, 119, true, [], [.. entries], [.. votes]);

    private static PublishedRecordEntry Entry(char id, PublishedRecordKind kind, DateOnly date) => new()
    {
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
        Congress = 119,
        Session = 1,
        RollCallNumber = 100,
        BillType = "hr",
        BillNumber = 7,
        Cast = "Yea",
        Headline = $"Vote {id}"
    };
}
