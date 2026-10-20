using AngleSharp.Html.Parser;
using CivicLens.Application.Publication;
using CivicLens.Host.Publication;
using CivicLens.Publication.Contracts;

namespace CivicLens.Tests.Host;

public sealed class PublishedOfficialPageTests
{
    [Fact]
    public async Task TheOfficialPageListsStatementsActionsAndVotesNewestFirstWithTheirOwnDates()
    {
        var official = Official(
            [Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1)), Entry('2', PublishedRecordKind.Action, new DateOnly(2026, 5, 1))],
            [Vote('3', new DateOnly(2026, 4, 1)), Vote('4', new DateOnly(2025, 12, 1))]);

        var html = await new ReleaseRenderer().RenderOfficialAsync(official, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        var records = document.QuerySelector("[aria-labelledby='panel-records']")!;
        Assert.Equal([new string('2', 64), new string('3', 64), new string('1', 64), new string('4', 64)],
            records.QuerySelectorAll("a.cell-title").Select(link => link.GetAttribute("href")!.Replace("../records/", "").Replace(".html", "")));
        Assert.Equal(["2026-05-01", "2026-04-01", "2026-03-01", "2025-12-01"], records.QuerySelectorAll("tbody td.num").Select(cell => cell.TextContent));
        Assert.Contains("Roll call 100, Congress 119, session 1, HR 7", records.TextContent, StringComparison.Ordinal);
        Assert.Contains("Yea", records.TextContent, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheOfficialPageShowsTheNameTheOfficesAndTheDatedTermsWithTableCaptions()
    {
        var official = Official([], []) with
        {
            Profile = new PublishedOfficialProfile
            {
                Id = "mayor",
                Name = "Mayor Example",
                Terms =
                [
                    new() { Office = "rep", State = "WA", District = 1, Party = "Democrat", Start = new(1993, 1, 3), End = new(2025, 1, 3) },
                    new() { Office = "sen", State = "WA", Party = "Democrat", Start = new(2025, 1, 3), End = new(2031, 1, 3) }
                ]
            }
        };

        var html = await new ReleaseRenderer().RenderOfficialAsync(official, CancellationToken.None);

        var document = new HtmlParser().ParseDocument(html);
        Assert.Equal("Mayor Example", document.QuerySelector("h1")!.TextContent);
        Assert.Equal(["Representative for WA, district 1", "Senator for WA"],
            document.QuerySelectorAll("[aria-labelledby='panel-offices'] tbody tr td:first-child").Select(cell => cell.TextContent));
        Assert.Equal(["1993-01-03", "2025-01-03"], document.QuerySelectorAll("[aria-labelledby='panel-offices'] tbody tr td.num:nth-child(3)").Select(cell => cell.TextContent));
        Assert.All(document.QuerySelectorAll("table"), table => Assert.NotNull(table.QuerySelector("caption")));
        Assert.Contains("href=\"../officials.html\"", html, StringComparison.Ordinal);
        Assert.Contains("href=\"../assets/css/theme.css\"", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnOfficialWithNoTermStatesThatTheRostersListNoOffice()
    {
        var html = await new ReleaseRenderer().RenderOfficialAsync(Official([Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1))], []),
            CancellationToken.None);

        Assert.Contains("The rosters list no dated office for this official.", html, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TheOfficialListLinksEachPageAndShowsTheRecordCount()
    {
        var html = await new ReleaseRenderer().RenderOfficialsAsync(
            [Official([Entry('1', PublishedRecordKind.Statement, new DateOnly(2026, 3, 1))], [Vote('3', new DateOnly(2026, 4, 1))])], CancellationToken.None);

        var row = new HtmlParser().ParseDocument(html).QuerySelector("tbody tr")!;
        Assert.Equal("officials/mayor.html", row.QuerySelector("a")!.GetAttribute("href"));
        Assert.Equal("2", row.QuerySelector("td.num")!.TextContent);
    }

    private static ReleaseOfficial Official(PublishedRecordEntry[] entries, PublishedOfficialVote[] votes) =>
        new(new PublishedOfficialProfile { Id = "mayor", Name = "Mayor Example" }, [.. entries], [.. votes]);

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
