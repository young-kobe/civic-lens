using System.Net;
using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Issues;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Issues;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class IssueEndpointsTests(PostgresCollection postgres)
{
    private async Task<ApiHost> StartAsync(SeededVote[] votes, SeededBill[] bills, params string[] issues)
    {
        var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "SET session_replication_role = replica;" +
            string.Concat(issues.Select(issue => $"INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (1, '{issue}', '{issue}', 'd');")));
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, votes);
        await BillsSeed.AddBillsAsync(host.OperationalDatabase, bills);
        return host;
    }

    private static async Task<JsonElement> GetAsync(ApiHost host, string issue, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await host.Client.GetAsync($"/v1/issues/{issue}");
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{issue} gave {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body).RootElement.Clone();
    }

    private static PublishedRecordEntry VoteEntry(SeededVote vote) => new()
    {
        Kind = PublishedRecordKind.Vote,
        RecordId = vote.RecordId,
        RevisionNumber = 1,
        Headline = "H",
        FirstPublishedAtUtc = DateTimeOffset.UnixEpoch,
        Officials = [],
        IssueIds = vote.Issues,
        PolicyAreaIds = [],
        Alignment = PublishedVoteAlignment.PartiesAgree
    };

    private static PublishedRecordEntry BillEntry(SeededBill bill) => new()
    {
        Kind = PublishedRecordKind.Action,
        RecordId = bill.RecordId,
        RevisionNumber = 1,
        Headline = "H",
        FirstPublishedAtUtc = DateTimeOffset.UnixEpoch,
        Officials = [],
        IssueIds = bill.Issues,
        PolicyAreaIds = []
    };

    private static SeededVote Vote(char id, string kind, params string[] issues) => new(id, "2026-03-01") { Kind = kind, RollCall = id, Issues = issues };

    private static SeededBill Bill(char id, params string[] issues) => new(id, "2026-03-01") { BillNumber = id, Issues = issues };

    private static SeededBill BillBy(char id, string[] issues, params string[] sponsors) =>
        new(id, "2026-03-01") { BillNumber = id, Issues = issues, Sponsorships = [.. sponsors.Select(person => new SeededSponsorship(person, "Sponsor", "2026-01-10"))] };

    [Fact]
    public async Task TheVoteAndBillCountsEqualTheFacetSummaryOfTheStaticIssuePageBecauseOneRuleMustGiveBothAnswers()
    {
        var votes = new[] { Vote('1', "Passage", "tax"), Vote('2', "Rule", "tax"), Vote('3', "Cloture", "tax", "health"), Vote('4', "Passage", "health") };
        var bills = new[] { Bill('a', "tax"), Bill('b', "tax", "health"), Bill('c', "health") };
        await using var host = await StartAsync(votes, bills, "tax", "health");

        var answer = await GetAsync(host, "tax");

        var entries = votes.Select(VoteEntry).Concat(bills.Select(BillEntry)).Where(entry => entry.IssueIds.Contains("tax"));
        var summary = FacetSummary.From(entries);
        Assert.Equal((summary.Votes, summary.Bills), (answer.GetProperty("votes").GetInt32(), answer.GetProperty("bills").GetInt32()));
        Assert.Equal(1, answer.GetProperty("release").GetInt32());
        Assert.Equal("tax", answer.GetProperty("issueId").GetString());
    }

    [Fact]
    public async Task SubstantiveVotesCountOnlyTheSubstantiveKindsOfTheContractRule()
    {
        var kinds = Enum.GetValues<PublishedVoteKind>();
        var votes = kinds.Select((kind, index) => Vote("0123456789abcdef"[index], kind.ToString(), "tax")).ToArray();
        await using var host = await StartAsync(votes, [], "tax");

        var answer = await GetAsync(host, "tax");

        Assert.Equal(kinds.Count(kind => kind.IsSubstantive()), answer.GetProperty("substantiveVotes").GetInt32());
        Assert.Equal(kinds.Length, answer.GetProperty("votes").GetInt32());
        Assert.NotEqual(kinds.Length, answer.GetProperty("substantiveVotes").GetInt32());
    }

    [Fact]
    public async Task OnlyRecordsOfTheActiveReleaseAndTrackedIssuesCount()
    {
        var votes = new[]
        {
            Vote('1', "Passage", "tax"), new SeededVote('2', "2026-03-01") { RollCall = 2, Issues = ["tax"], FirstRelease = 2 },
            new SeededVote('3', "2026-03-01") { RollCall = 3, Issues = ["tax"], FirstRelease = 0, LastRelease = 0 },
            new SeededVote('4', "2026-03-01") { RollCall = 4, UntrackedIssues = ["tax"] }
        };
        var bills = new[] { Bill('a', "tax"), new SeededBill('b', "2026-03-01") { BillNumber = 2, Issues = ["tax"], FirstRelease = 2 }, new SeededBill('c', "2026-03-01") { BillNumber = 3, UntrackedIssues = ["tax"] } };
        await using var host = await StartAsync(votes, bills, "tax");

        var answer = await GetAsync(host, "tax");

        Assert.Equal((1, 1), (answer.GetProperty("votes").GetInt32(), answer.GetProperty("bills").GetInt32()));
    }

    [Fact]
    public async Task TopSponsorsRankSponsoredBillsMostFirstThenByOfficialIdCountOnlyTheRoleSponsorAndNameTheBaseOfAllBillsOnTheIssue()
    {
        var bills = new[]
        {
            BillBy('a', ["tax"], "p2"), BillBy('b', ["tax"], "p2"), BillBy('c', ["tax"], "p3"), BillBy('d', ["tax"], "p1"),
            BillBy('e', ["health"], "p1"), BillBy('f', ["health"], "p1"),
            // The default seed has the sponsor p1 and the cosponsor p2. Only p1 counts for this bill.
            Bill('g', "tax")
        };
        await using var host = await StartAsync([Vote('1', "Passage", "tax")], bills, "tax", "health");

        var answer = await GetAsync(host, "tax");

        var sponsors = answer.GetProperty("topSponsors").EnumerateArray()
            .Select(item => (item.GetProperty("officialId").GetString(), item.GetProperty("sponsoredBills").GetInt32(), item.GetProperty("base").GetInt32())).ToArray();
        Assert.Equal([("p1", 2, 5), ("p2", 2, 5), ("p3", 1, 5)], sponsors);
    }

    [Fact]
    public async Task TheListStopsAtTheDocumentedLimit()
    {
        var bills = Enumerable.Range(0, IssueStore.MaximumSponsors + 2).Select(index => BillBy((char)('a' + index), ["tax"], $"p{index}")).ToArray();
        await using var host = await StartAsync([], bills, "tax");

        var answer = await GetAsync(host, "tax");

        Assert.Equal(IssueStore.MaximumSponsors, answer.GetProperty("topSponsors").GetArrayLength());
        Assert.Equal(Enumerable.Range(0, IssueStore.MaximumSponsors).Select(index => $"p{index}"),
            answer.GetProperty("topSponsors").EnumerateArray().Select(item => item.GetProperty("officialId").GetString()));
    }

    [Fact]
    public async Task AnIssueWithNoRecordsGivesZerosAndAnUnknownIssueGives404()
    {
        await using var host = await StartAsync([Vote('1', "Passage", "tax")], [], "tax", "quiet");

        var quiet = await GetAsync(host, "quiet");
        await GetAsync(host, "nothing", HttpStatusCode.NotFound);

        Assert.Equal((0, 0, 0, 0), (quiet.GetProperty("votes").GetInt32(), quiet.GetProperty("substantiveVotes").GetInt32(), quiet.GetProperty("bills").GetInt32(),
            quiet.GetProperty("topSponsors").GetArrayLength()));
    }
}
