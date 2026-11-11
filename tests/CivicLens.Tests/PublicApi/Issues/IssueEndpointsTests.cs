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
    public async Task TheBillCountEqualsTheFacetSummaryOfTheStaticIssuePageBecauseOneRuleMustGiveBothAnswers()
    {
        var votes = new[] { Vote('1', "Passage", "tax"), Vote('2', "Rule", "tax"), Vote('3', "Cloture", "tax", "health"), Vote('4', "Passage", "health") };
        var bills = new[] { Bill('a', "tax"), Bill('b', "tax", "health"), Bill('c', "health") };
        await using var host = await StartAsync(votes, bills, "tax", "health");

        var answer = await GetAsync(host, "tax");

        var entries = votes.Select(VoteEntry).Concat(bills.Select(BillEntry)).Where(entry => entry.IssueIds.Contains("tax"));
        var summary = FacetSummary.From(entries);
        Assert.Equal(summary.Bills, answer.GetProperty("bills").GetInt32());
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
        Assert.NotEqual(kinds.Length, answer.GetProperty("substantiveVotes").GetInt32());
        Assert.False(answer.TryGetProperty("votes", out _));
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

        Assert.Equal((1, 1), (answer.GetProperty("substantiveVotes").GetInt32(), answer.GetProperty("bills").GetInt32()));
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

    private static SeededBill BillOn(char id, string day, string sponsor) =>
        new(id, day) { BillNumber = id, Issues = ["tax"], Sponsorships = [new(sponsor, "Sponsor", "2025-02-01")] };

    private static PublishedRecordEntry SponsoredEntry(SeededBill bill) => new()
    {
        Kind = PublishedRecordKind.Action,
        RecordId = bill.RecordId,
        RevisionNumber = 1,
        Headline = "H",
        FirstPublishedAtUtc = DateTimeOffset.UnixEpoch,
        Officials = [.. bill.Sponsorships.Where(sponsorship => sponsorship.Role == "Sponsor").Select(sponsorship => new PublishedOfficial { Id = sponsorship.Person, Name = sponsorship.Person })],
        IssueIds = bill.Issues,
        PolicyAreaIds = [],
        EventDate = DateOnly.Parse(bill.EventOn, System.Globalization.CultureInfo.InvariantCulture)
    };

    [Fact]
    public async Task SponsoredBillsCountOnlyTheCurrentCongressAsTheStaticIssuePaneDoesAndTheEdgeDaysDecideTheCongress()
    {
        // The release is from 2 June 2026, in the 119th Congress, which runs from 3 January 2025 to 2 January 2027.
        var bills = new[]
        {
            BillOn('a', "2025-01-02", "p1"), BillOn('b', "2025-01-03", "p1"), BillOn('c', "2026-03-01", "p1"), BillOn('d', "2027-01-02", "p1"),
            BillOn('e', "2027-01-03", "p1"), BillOn('f', "2023-05-01", "p2"), BillOn('g', "2026-04-01", "p2")
        };
        await using var host = await StartAsync([], bills, "tax");

        var answer = await GetAsync(host, "tax");

        var served = answer.GetProperty("topSponsors").EnumerateArray()
            .ToDictionary(item => item.GetProperty("officialId").GetString()!, item => item.GetProperty("sponsoredBills").GetInt32());
        var entries = bills.Select(SponsoredEntry).ToArray();
        var expected = new[] { "p1", "p2" }.ToDictionary(official => official,
            official => TalkAndActionProfile.Of(119, entries.Where(entry => entry.Officials.Any(person => person.Id == official)), []).SponsoredOn("tax").Length);
        Assert.Equal(expected, served);
        Assert.Equal((3, 1), (served["p1"], served["p2"]));
        Assert.All(answer.GetProperty("topSponsors").EnumerateArray(), item => Assert.Equal(bills.Length, item.GetProperty("base").GetInt32()));
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

        Assert.Equal((0, 0, 0), (quiet.GetProperty("substantiveVotes").GetInt32(), quiet.GetProperty("bills").GetInt32(), quiet.GetProperty("topSponsors").GetArrayLength()));
    }

    private static Task SeedLineUpAsync(ApiHost host, int bills, int unknown, params (string Party, int Bills)[] parties) => ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
        SET session_replication_role = replica;
        UPDATE release_issues SET count_congress = 119, substantive_votes = 0, bills = {bills} WHERE release_number = 1 AND issue_id = 'tax';
        INSERT INTO release_issue_party_lineups (release_number, issue_id, unknown_bills) VALUES (1, 'tax', {unknown});
        {string.Concat(parties.Select(part => $"INSERT INTO release_issue_party_counts (release_number, issue_id, party, bills) VALUES (1, 'tax', '{part.Party}', {part.Bills});"))}
        """);

    [Fact]
    public async Task TheLineUpNamesTheCongressTheBaseTheBillsOfEachPartyMostFirstAndTheUnknownCountThatAddUpToTheBase()
    {
        await using var host = await StartAsync([], [], "tax");
        await SeedLineUpAsync(host, 7, 1, ("Democratic", 2), ("Republican", 3), ("Independent", 1));

        var lineUp = (await GetAsync(host, "tax")).GetProperty("lineUp");

        Assert.Equal((119, 7, 1), (lineUp.GetProperty("congress").GetInt32(), lineUp.GetProperty("bills").GetInt32(), lineUp.GetProperty("unknownBills").GetInt32()));
        var parties = lineUp.GetProperty("parties").EnumerateArray().Select(item => (item.GetProperty("party").GetString(), item.GetProperty("bills").GetInt32())).ToArray();
        Assert.Equal([("Republican", 3), ("Democratic", 2), ("Independent", 1)], parties);
        Assert.Equal(7, parties.Sum(part => part.Item2) + lineUp.GetProperty("unknownBills").GetInt32());
    }

    [Fact]
    public async Task AnIssueWithOnlyUnknownBillsHasNoPartiesAndAZeroBillIssueHasAnEmptyLineUp()
    {
        await using var host = await StartAsync([], [], "tax", "quiet");
        await SeedLineUpAsync(host, 2, 2);

        var lineUp = (await GetAsync(host, "tax")).GetProperty("lineUp");

        Assert.Equal((2, 0), (lineUp.GetProperty("unknownBills").GetInt32(), lineUp.GetProperty("parties").GetArrayLength()));
    }

    [Fact]
    public async Task AReleaseWithNoLineUpRowsGivesANullLineUpAndNeverZerosBecauseAnOlderReleaseHoldsNoSnapshot()
    {
        await using var host = await StartAsync([], [], "tax");
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "UPDATE release_issues SET count_congress = 119, substantive_votes = 0, bills = 4 WHERE issue_id = 'tax'");

        var answer = await GetAsync(host, "tax");

        Assert.Equal(JsonValueKind.Null, answer.GetProperty("lineUp").ValueKind);
    }

    [Fact]
    public async Task ALineUpThatDoesNotAddUpToTheHomeTileCountFailsTheRequestBecauseAWrongSplitMustNeverShow()
    {
        await using var host = await StartAsync([], [], "tax");
        await SeedLineUpAsync(host, 7, 1, ("Democratic", 2), ("Republican", 3));

        using var response = await host.Client.GetAsync("/v1/issues/tax");

        Assert.Equal(HttpStatusCode.InternalServerError, response.StatusCode);
    }
}
