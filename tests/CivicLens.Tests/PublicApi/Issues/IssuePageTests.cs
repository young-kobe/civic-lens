using System.Net;
using AngleSharp.Dom;
using AngleSharp.Html.Parser;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Issues;
using CivicLens.PublicApi.Pages;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Votes;
using CivicLens.Tests.PublicApi.ZipDistricts;
using Npgsql;

namespace CivicLens.Tests.PublicApi.Issues;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class IssuePageTests(PostgresCollection postgres)
{
    private const string Path = "/issues/tax.html";

    private async Task<ApiHost> StartAsync(SeededVote[] votes, SeededBill[] bills)
    {
        var host = await ApiHost.StartAsync(postgres, options => options with
        {
            PublicBaseUrl = PublicBaseUrl.Parse("https://civic-lens.test", "test"),
            BrowserApiUrl = PublicBaseUrl.Parse("https://api.civic-lens.test", "test")
        });
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;" +
            "INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (1, 'tax', 'Taxes <b>', 'd');" +
            "UPDATE release_issues SET count_congress = 119, substantive_votes = 0, bills = 0 WHERE issue_id = 'tax';");
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
        Assert.Contains("1 substantive roll call vote and 0 bills and resolutions tagged Taxes <b> in the 119th Congress.", page.QuerySelector("[data-issue-counts]")!.TextContent);
        Assert.Contains("not about the whole issue", page.QuerySelector("#votes .issue-note")!.TextContent);
    }

    [Fact]
    public async Task TheCountsAndTheListsCoverOnlyTheCongressOfTheReleaseAndTheSplitCountReadsTheStoredAlignment()
    {
        var votes = new[]
        {
            new SeededVote('1', "2026-03-01") { Kind = "Passage", RollCall = 1, Issues = ["tax"] },
            new SeededVote('2', "2026-04-01") { Kind = "Passage", RollCall = 2, Issues = ["tax"] },
            new SeededVote('3', "2024-04-01") { Kind = "Passage", RollCall = 3, Issues = ["tax"], Congress = 118 }
        };
        var bills = new[]
        {
            new SeededBill('a', "2026-03-01") { BillNumber = 1, Issues = ["tax"] },
            new SeededBill('b', "2024-03-01") { BillNumber = 2, Issues = ["tax"], Congress = 118 }
        };
        await using var host = await StartAsync(votes, bills);
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;" +
            "UPDATE vote_revisions SET alignment = 'Split', democrat_majority = 'Yea', republican_majority = 'Nay', columns_written = true WHERE congress = 119 AND roll_call_number = 1;" +
            "UPDATE vote_revisions SET alignment = 'Split', democrat_majority = 'Yea', republican_majority = 'Nay', columns_written = true WHERE congress = 118;");

        var page = await GetPageAsync(host);

        Assert.Equal("This release holds 2 substantive roll call votes and 1 bill or resolution tagged Taxes <b> in the 119th Congress.", page.QuerySelector("[data-issue-counts]")!.TextContent);
        Assert.StartsWith("1 of those 2 votes was a split vote", page.QuerySelector("[data-issue-split]")!.TextContent);
        Assert.Equal(2, page.QuerySelectorAll("#votes .record-row").Length);
        Assert.Equal(1, page.QuerySelectorAll("#bills .record-row").Length);
    }

    [Fact]
    public async Task TheScopePanelShowsTheStoredIncludesAndExcludesInStoredOrderAndOnlyOfThisIssue()
    {
        await using var host = await StartAsync([], []);
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;" +
            "INSERT INTO release_issue_includes (release_number, issue_id, \"position\", text) VALUES (1, 'tax', 1, 'Second'), (1, 'tax', 0, 'First <i>'), (1, 'other', 0, 'Elsewhere');" +
            "INSERT INTO release_issue_excludes (release_number, issue_id, \"position\", text) VALUES (1, 'tax', 0, 'Not this');");

        var page = await GetPageAsync(host);

        Assert.Equal(["First <i>", "Second"], page.QuerySelectorAll(".issue-scope-card:first-child li").Select(item => item.TextContent));
        Assert.Equal(["Not this"], page.QuerySelectorAll(".issue-scope-card:last-child li").Select(item => item.TextContent));
        Assert.DoesNotContain("Elsewhere", page.DocumentElement.TextContent);
        Assert.Null(page.QuerySelector(".issue-scope-card i"));
    }

    [Fact]
    public async Task TheVoteListStopsAtTheNewestTenAndNamesTheTotal()
    {
        var votes = Enumerable.Range(1, 12).Select(index => new SeededVote("0123456789abcdef"[index], $"2026-03-{index:00}") { Kind = "Passage", RollCall = index, Issues = ["tax"] }).ToArray();
        await using var host = await StartAsync(votes, []);

        var page = await GetPageAsync(host);

        Assert.Equal(IssueStore.PageVotes, page.QuerySelectorAll("#votes .record-row").Length);
        Assert.Contains("The 10 newest of 12.", page.QuerySelector("#votes")!.TextContent);
    }

    [Fact]
    public async Task TheBillListStopsAtTheNewestFiveAndNamesTheTotal()
    {
        var bills = Enumerable.Range(1, 7).Select(index => new SeededBill((char)('a' + index), $"2026-03-{index:00}") { BillNumber = index, Issues = ["tax"] }).ToArray();
        await using var host = await StartAsync([], bills);

        var page = await GetPageAsync(host);

        Assert.Equal(IssueStore.PageBills, page.QuerySelectorAll("#bills .record-row").Length);
        Assert.Contains("The 5 newest of 7.", page.QuerySelector("#bills")!.TextContent);
    }

    [Fact]
    public async Task TheBillsComeInTheOrderOfTheBillListOfTheIssueSoTheyAreTheFirstRowsOfSeeAllAndEachShowsTheSameLatestActionLine()
    {
        // The event dates fall in the reverse order of the latest actions. Bills 2 and 4 tie on the action date, and bill 3 has no stored status. Bill 9 has the newest action and no issue.
        (char Id, string? ActionOn)[] stored = [('1', "2026-01-01"), ('2', "2026-02-10"), ('3', null), ('4', "2026-02-10"), ('5', "2026-02-20"), ('6', "2026-01-15"), ('7', "2026-02-01")];
        var bills = stored.Select((bill, index) => new SeededBill(bill.Id, $"2026-03-{7 - index:00}") { BillNumber = index + 1, Issues = ["tax"] })
            .Append(new SeededBill('9', "2026-03-09") { BillNumber = 9 }).ToArray();
        await using var host = await StartAsync([], bills);
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;" + string.Concat(stored.Append((Id: '9', ActionOn: (string?)"2026-03-01")).Where(bill => bill.ActionOn is not null)
            .Select(bill => PublicRecordPageRows.PinnedStatus(1, new string(bill.Id, 64), "Referred to the House Committee on Ways and Means.", bill.ActionOn!, 1, "InCommittee"))));
        await StoredBillCounts.FillAsync(host.OperationalDatabase, 1, 119);

        var issueRows = (await GetPageAsync(host)).QuerySelectorAll("#bills .record-row");
        var listRows = (await GetPageAsync(host, "/bills.html?issue=tax")).QuerySelectorAll(".record-row");

        Assert.Equal(["5", "2", "4", "7", "6"], issueRows.Select(BillId));
        Assert.Equal(["5", "2", "4", "7", "6", "1", "3"], listRows.Select(BillId));
        Assert.All(issueRows.Zip(listRows), pair => Assert.Contains(pair.Second.QuerySelector("p.small.muted")!.TextContent, pair.First.TextContent));
        Assert.Contains("Latest official action 20 Feb 2026: In committee: sent to the House Committee on Ways and Means.", issueRows[0].TextContent);
    }

    private static string BillId(IElement row) => row.QuerySelector("a")!.GetAttribute("href")!.Split("records/")[1][..1];

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
    public async Task AStoredBillLineThatEndsWithTheCutMarkShowsNoLineAndNoSourceNote()
    {
        var bill = new SeededBill('a', "2026-03-01") { BillNumber = 1, Issues = ["tax"], BillLine = PublishedSamples.CutBillLine };
        await using var host = await StartAsync([], [bill]);

        var row = Assert.Single((await GetPageAsync(host)).QuerySelectorAll("#bills .record-row"));

        Assert.Null(row.QuerySelector(".bill-line"));
        Assert.DoesNotContain(PublishedWording.BillLineSource, row.TextContent);
    }

    [Fact]
    public async Task AVoteRowAndABillRowColorTheirLabelAsAiOnlyWhenTheViewSaysAModelWroteTheText()
    {
        var votes = new[]
        {
            new SeededVote('1', "2026-03-02") { RollCall = 1, Issues = ["tax"], TextOrigin = "Model" },
            new SeededVote('2', "2026-03-01") { RollCall = 2, Issues = ["tax"], TextOrigin = "Code" }
        };
        var bills = new[]
        {
            new SeededBill('a', "2026-03-02") { BillNumber = 1, Issues = ["tax"], TextOrigin = "Model" },
            new SeededBill('b', "2026-03-01") { BillNumber = 2, Issues = ["tax"], TextOrigin = "Code" }
        };
        await using var host = await StartAsync(votes, bills);

        var page = await GetPageAsync(host);

        foreach (var section in new[] { "#votes", "#bills" })
        {
            var labels = page.QuerySelectorAll($"{section} .ai-label");
            Assert.Equal(["model", "code"], labels.Select(label => label.GetAttribute("data-origin")));
            Assert.All(labels, label => Assert.NotEmpty(label.TextContent));
        }
    }

    [Fact]
    public async Task ABillRowNamesItsSponsorAndNotItsCosponsorAndAnUnnamedSponsorShowsNoSponsorLine()
    {
        var named = new SeededBill('a', "2026-03-01") { BillNumber = 1, Issues = ["tax"], Sponsorships = [new("p2", "Cosponsor", "2026-01-10"), new("p1", "Sponsor", "2026-01-10")] };
        var unnamed = new SeededBill('b', "2026-02-01") { BillNumber = 2, Issues = ["tax"], Sponsorships = [new("p9", "Sponsor", "2026-01-10")] };
        await using var host = await StartAsync([], [named, unnamed]);
        await ZipSeed.AddNamesAsync(host.OperationalDatabase, 1,
            new SeededSeat("p1", "Rosa Sponsor", "Representative", "CT", 3), new SeededSeat("p2", "Gabe Cosponsor", "Representative", "RI", 2));

        var rows = (await GetPageAsync(host)).QuerySelectorAll("#bills .record-row");

        Assert.Contains("Sponsor: Rosa Sponsor", rows[0].TextContent);
        Assert.DoesNotContain("Gabe Cosponsor", rows[0].TextContent);
        Assert.DoesNotContain("Sponsor:", rows[1].TextContent);
    }

    [Fact]
    public async Task ThePartyLineUpNamesItsBaseCongressAndUnknownCountAndTheSplitAddsUpToTheBase()
    {
        await using var host = await StartAsync([], []);
        await SeedLineUpAsync(host, 7, 1, ("Democrat", 2), ("Republican", 3), ("Independent", 1));

        var page = await GetPageAsync(host);

        var section = page.QuerySelector("#parties")!;
        Assert.Contains("7 bills and resolutions tagged Taxes <b> in the 119th Congress", section.TextContent);
        var rows = section.QuerySelectorAll(".party-bar-legend li").Select(row => Compact(row.TextContent)).ToArray();
        Assert.Equal(["2 Democrat sponsor", "3 Republican sponsor", "1 Independent sponsor", "1 Sponsor party not known"], rows);
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

        using var unknown = await host.Client.GetAsync("/issues/nothing.html");
        using var oversized = await host.Client.GetAsync("/issues/" + new string('x', 300) + ".html");

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
