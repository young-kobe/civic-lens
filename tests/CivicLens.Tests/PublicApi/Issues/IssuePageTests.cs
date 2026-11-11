using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Issues;
using CivicLens.PublicApi.Pages;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Votes;
using Npgsql;

namespace CivicLens.Tests.PublicApi.Issues;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class IssuePageTests(PostgresCollection postgres)
{
    private const string Path = ServerPageEndpoints.Prefix + "/issues/tax";

    private async Task<ApiHost> StartAsync(SeededVote[] votes, SeededBill[] bills)
    {
        var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite(), options => options with
        {
            PublicBaseUrl = PublicBaseUrl.Parse("https://civic-lens.test", "test"),
            BrowserApiUrl = PublicBaseUrl.Parse("https://api.civic-lens.test", "test")
        });
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;" +
            "INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (1, 'tax', 'Taxes <b>', 'd');");
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, votes);
        await BillsSeed.AddBillsAsync(host.OperationalDatabase, bills);
        return host;
    }

    private static async Task<IDocument> GetPageAsync(ApiHost host, string path = Path)
    {
        using var response = await host.Client.GetAsync(path);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return new HtmlParser().ParseDocument(await response.Content.ReadAsStringAsync());
    }

    private static Task SeedLineUpAsync(ApiHost host, int bills, int unknown, params (string Party, int Bills)[] parties) => ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
        SET session_replication_role = replica;
        UPDATE release_issues SET count_congress = 119, substantive_votes = 0, bills = {bills} WHERE release_number = 1 AND issue_id = 'tax';
        INSERT INTO release_issue_party_lineups (release_number, issue_id, unknown_bills) VALUES (1, 'tax', {unknown});
        {string.Concat(parties.Select(part => $"INSERT INTO release_issue_party_counts (release_number, issue_id, party, bills) VALUES (1, 'tax', '{part.Party}', {part.Bills});"))}
        """);

    [Fact]
    public async Task ThePageListsOnlySubstantiveVotesOfTheIssueNewestFirstAndEscapesTheIssueName()
    {
        var votes = new[]
        {
            new SeededVote('1', "2026-03-01") { Kind = "Passage", RollCall = 1, Issues = ["tax"] },
            new SeededVote('2', "2026-04-01") { Kind = "Rule", RollCall = 2, Issues = ["tax"] },
            new SeededVote('3', "2026-05-01") { Kind = "Cloture", RollCall = 3, Issues = ["tax"] },
            new SeededVote('4', "2026-06-01") { Kind = "Passage", RollCall = 4, Issues = ["health"] }
        };
        await using var host = await StartAsync(votes, []);

        var page = await GetPageAsync(host);

        Assert.Equal("Taxes <b>", page.QuerySelector("h1")!.TextContent);
        Assert.Null(page.QuerySelector("h1 b"));
        Assert.Equal(1, page.QuerySelectorAll("#votes .record-row").Length);
        Assert.DoesNotContain("Showing the newest", page.QuerySelector("#votes")!.TextContent);
        Assert.Contains("1 substantive roll call vote and 0 bills and resolutions tagged Taxes <b>.", page.QuerySelector("[data-issue-counts]")!.TextContent);
        Assert.Contains("not about the whole issue", page.QuerySelector("#votes .issue-note")!.TextContent);
    }

    [Fact]
    public async Task TheVoteListStopsAtTheNewestTenAndNamesTheTotal()
    {
        var votes = Enumerable.Range(1, 12).Select(index => new SeededVote("0123456789abcdef"[index], $"2026-03-{index:00}") { Kind = "Passage", RollCall = index, Issues = ["tax"] }).ToArray();
        await using var host = await StartAsync(votes, []);

        var page = await GetPageAsync(host);

        Assert.Equal(IssueStore.PageVotes, page.QuerySelectorAll("#votes .record-row").Length);
        Assert.Contains("Showing the newest 10 of 12 substantive roll call votes.", page.QuerySelector("#votes")!.TextContent);
    }

    [Fact]
    public async Task TheBillListStopsAtTheNewestFiveAndNamesTheTotal()
    {
        var bills = Enumerable.Range(1, 7).Select(index => new SeededBill((char)('a' + index), $"2026-03-{index:00}") { BillNumber = index, Issues = ["tax"] }).ToArray();
        await using var host = await StartAsync([], bills);

        var page = await GetPageAsync(host);

        Assert.Equal(IssueStore.PageBills, page.QuerySelectorAll("#bills .record-row").Length);
        Assert.Contains("Showing the newest 5 of 7 bills and resolutions.", page.QuerySelector("#bills")!.TextContent);
    }

    [Fact]
    public async Task ABillShowsItsBillLineWithTheSourceAndLinksToItsRecordPage()
    {
        var bill = new SeededBill('a', "2026-03-01") { BillNumber = 1, Issues = ["tax"], BillLine = "Current bill line." };
        await using var host = await StartAsync([], [bill, new SeededBill('b', "2026-02-01") { BillNumber = 2, Issues = ["tax"] }]);

        var rows = (await GetPageAsync(host)).QuerySelectorAll("#bills .record-row");

        Assert.Contains("Current bill line.", rows[0].QuerySelector(".bill-line")!.TextContent);
        Assert.Contains(PublishedWording.BillLineSource, rows[0].TextContent);
        Assert.DoesNotContain(PublishedWording.BillLineSource, rows[1].TextContent);
        Assert.Equal("https://civic-lens.test/" + PublicationProtocol.RecordPagePath(bill.RecordId), rows[0].QuerySelector("a")!.GetAttribute("href"));
    }

    [Fact]
    public async Task ThePartyLineUpNamesItsBaseCongressAndUnknownCountAndTheSplitAddsUpToTheBase()
    {
        await using var host = await StartAsync([], []);
        await SeedLineUpAsync(host, 7, 1, ("Democratic", 2), ("Republican", 3), ("Independent", 1));

        var page = await GetPageAsync(host);

        var section = page.QuerySelector("#parties")!;
        Assert.Contains("7 bills and resolutions tagged Taxes <b> in the 119th Congress", section.TextContent);
        var rows = section.QuerySelectorAll("[data-issue-lineup] li").Select(row => Compact(row.TextContent)).ToArray();
        Assert.Equal(["Republican sponsor 3 bills", "Democratic sponsor 2 bills", "Independent sponsor 1 bill", "Sponsor party not known 1 bill"], rows);
    }

    [Fact]
    public async Task AReleaseWithNoLineUpShowsNoPartySectionAndNoZeros()
    {
        await using var host = await StartAsync([], []);
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "UPDATE release_issues SET count_congress = 119, substantive_votes = 0, bills = 4 WHERE issue_id = 'tax'");

        var page = await GetPageAsync(host);

        Assert.Null(page.QuerySelector("#parties"));
        Assert.DoesNotContain("Sponsor party not known", page.DocumentElement.TextContent);
    }

    [Fact]
    public async Task TheSharedPageHoldsNoZipAndTheMembersSlotCarriesOnlyTheIssueAndTheApiOrigin()
    {
        await using var host = await StartAsync([], []);

        using var response = await host.Client.GetAsync(Path + "?zip=55401");
        var page = new HtmlParser().ParseDocument(await response.Content.ReadAsStringAsync());

        var slot = page.QuerySelector("[data-issue-members]")!;
        Assert.Equal("tax", slot.GetAttribute("data-issue-members"));
        Assert.Equal("https://api.civic-lens.test", slot.GetAttribute("data-members-api"));
        Assert.True(slot.HasAttribute("hidden"));
        Assert.Equal(0, slot.ChildElementCount);
        Assert.DoesNotContain("55401", page.DocumentElement.OuterHtml);
        Assert.Equal("https://civic-lens.test/issues/tax.html", page.QuerySelector("link[rel=canonical]")!.GetAttribute("href"));
    }

    [Fact]
    public async Task AnUnknownOrOversizedIssueAnswers404()
    {
        await using var host = await StartAsync([], []);

        using var unknown = await host.Client.GetAsync(ServerPageEndpoints.Prefix + "/issues/nothing");
        using var oversized = await host.Client.GetAsync(ServerPageEndpoints.Prefix + "/issues/" + new string('x', 300));

        Assert.Equal(HttpStatusCode.NotFound, unknown.StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, oversized.StatusCode);
    }

    [Fact]
    public async Task AnOversizedIssueIdIsRefusedBeforeAnyConnectionIsOpened()
    {
        await using var unreachable = NpgsqlDataSource.Create("Host=unreachable.invalid;Timeout=1;Command Timeout=1");
        var reader = new IssuePageReader(new IssueStore(unreachable));

        Assert.Null(await reader.ReadAsync(new string('x', PublishedRecordEntry.MaximumFilterValueLength + 1), default));
        await Assert.ThrowsAnyAsync<Exception>(() => reader.ReadAsync("tax", default));
    }

    [Fact]
    public async Task TheVoteListAndTheCountLineUseOneFilterSoAVoteWithNoMemberCountsIsListedAndCounted()
    {
        var counted = new SeededVote('1', "2026-03-01") { Kind = "Passage", RollCall = 1, Issues = ["tax"] };
        var withOtherCasts = new SeededVote('2', "2026-04-01") { Kind = "Passage", RollCall = 2, Issues = ["tax"], Casts = ["Guilty", "Not Guilty"] };
        await using var host = await StartAsync([counted, withOtherCasts], []);

        var page = await GetPageAsync(host);

        Assert.Contains("2 substantive roll call votes", page.QuerySelector("[data-issue-counts]")!.TextContent);
        Assert.Equal(2, page.QuerySelectorAll("#votes .record-row").Length);
        Assert.DoesNotContain("Showing the newest", page.QuerySelector("#votes")!.TextContent);
        Assert.Equal(1, page.QuerySelectorAll("#votes .record-counts").Count(counts => counts.QuerySelector("b") is null));
    }

    [Fact]
    public async Task TheDefinitionOfTheIssueComesFromTheReleaseAndFollowsTheName()
    {
        await using var host = await StartAsync([], []);

        var page = await GetPageAsync(host);

        Assert.Equal("d", page.QuerySelector(".issue-hero h1 + .issue-definition")!.TextContent);
    }

    [Fact]
    public async Task TheIssuePageCarriesTheETagOfTheActiveReleaseAndAnswers304ForIt()
    {
        await using var host = await StartAsync([], []);
        using var first = await host.Client.GetAsync(Path);
        using var request = new HttpRequestMessage(HttpMethod.Get, Path);
        request.Headers.IfNoneMatch.Add(first.Headers.ETag!);

        using var second = await host.Client.SendAsync(request);

        Assert.StartsWith("W/\"r1-", first.Headers.ETag!.ToString());
        Assert.Equal(HttpStatusCode.NotModified, second.StatusCode);
    }

    private static string Compact(string text) => string.Join(' ', text.Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries));
}
