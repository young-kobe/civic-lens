using System.Net;
using System.Text.Json;
using CivicLens.Application.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Officials;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class OfficialEndpointsTests(PostgresCollection postgres)
{
    private static readonly SeededOfficial[] Officials =
    [
        new("p0", "Ann Able", new SeededTerm("Senator", "Senator", "2019-01-03") { State = "MA", Party = "Democrat", EndsOn = "2031-01-03" }),
        new("p1", "Ben Baker", new SeededTerm("Representative", "Representative", "2025-01-03") { State = "TX", District = 3, Party = "Republican", EndsOn = "2027-01-03" }),
        new("p2", "Cy 100% Cole", new SeededTerm("Senator", "Senator", "2021-01-03") { State = "TX", Party = "Republican", Caucus = "Democrat", EndsOn = "2027-01-03" }),
        new("p3", "Dee Dole", new SeededTerm("Representative", "Representative", "2025-01-03") { State = "MA", District = 1, Party = "Democrat", EndsOn = "2027-01-03" }),
        new("p4", "Eve East", new SeededTerm("President", "Executive", "2025-01-20") { Party = "Republican" }),
        new("p5", "Fay Fox-Gray")
    ];

    private static readonly SeededVote[] Votes =
    [
        new('a', "2026-02-01") { Casts = ["Yea", "Not Voting", "Nay", "Present", "Aye"], Issues = ["health"], RollCall = 1 },
        new('b', "2026-02-03") { Casts = ["Yea", "Yea", "Not Voting", "Yea", "Yea"], Issues = ["health", "tax"], Kind = "Rule", RollCall = 2 },
        new('c', "2026-02-05") { Casts = ["Nay", "Yea", "Yea", "Not Voting", "Nay"], Issues = ["tax"], Chamber = "Senate", RollCall = 3 },
        new('d', "2024-05-01") { Casts = ["Yea", "Yea", "Yea", "Yea", "Yea"], Issues = ["tax"], Congress = 118, RollCall = 4 }
    ];

    private static readonly SeededBill[] Bills =
    [
        new('1', "2026-03-02") { BillNumber = 1, Issues = ["health"], Sponsorships = [new("p0", "Sponsor", "2026-01-10"), new("p1", "Cosponsor", "2026-01-10"), new("p3", "Cosponsor", "2026-01-11", "2026-02-01", false)] },
        new('2', "2026-03-03") { BillNumber = 2, Issues = ["health", "tax"], Sponsorships = [new("p0", "Sponsor", "2026-03-03"), new("p1", "Cosponsor", "2026-03-03")] },
        new('3', "2024-05-01") { BillNumber = 3, Congress = 118, Issues = ["tax"], Sponsorships = [new("p0", "Sponsor", "2024-05-01")] }
    ];

    private async Task<ApiHost> StartAsync(bool withVotesAndBills = false, bool storeBillCounts = true)
    {
        var host = await ApiHost.StartAsync(postgres);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase, Officials);
        await OfficialsSeed.AddEntriesAsync(host.OperationalDatabase, new SeededEntry("statement", 'a', "p0", "p1"), new SeededEntry("statement", 'b', "p0"),
            new SeededEntry("executiveAction", 'c', "p4") { DocumentType = "ExecutiveOrder" }, new SeededEntry("executiveAction", 'd', "p4") { DocumentType = "Proclamation" },
            new SeededEntry("executiveAction", 'e', "p4") { DocumentType = "ExecutiveOrder" });
        if (!withVotesAndBills) return host;
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, Votes);
        await BillsSeed.AddBillsAsync(host.OperationalDatabase, Bills);
        if (storeBillCounts) await StoredBillCounts.FillAsync(host.OperationalDatabase, 1, 119);
        return host;
    }

    private static async Task<JsonDocument> GetAsync(ApiHost host, string path, HttpStatusCode expected = HttpStatusCode.OK)
    {
        using var response = await host.Client.GetAsync(path);
        var body = await response.Content.ReadAsStringAsync();
        Assert.True(response.StatusCode == expected, $"{path} gave {(int)response.StatusCode}: {body}");
        return JsonDocument.Parse(body);
    }

    private static string[] Ids(JsonDocument page) =>
        [.. page.RootElement.GetProperty("officials").EnumerateArray().Select(row => row.GetProperty("officialId").GetString()!)];

    private static PublishedOfficialVote PartyVote(char id, PublishedChamber chamber, int congress, string votedOn, string cast, string party, string partyMajority) => new()
    {
        RecordId = new(id, 64),
        Chamber = chamber,
        Congress = congress,
        Session = 1,
        RollCallNumber = id,
        VotedOn = DateOnly.Parse(votedOn),
        Cast = cast,
        Party = party,
        PartyMajority = partyMajority,
        Result = "Passed",
        SplitVote = true,
        Headline = "Vote",
        Kind = PublishedVoteKind.Passage,
        IssueIds = []
    };

    [Fact]
    public async Task OfficialsComeInNameOrderWithTheLatestTermTheRecordCountAndTheScopeOfTheCount()
    {
        await using var host = await StartAsync();

        using var page = await GetAsync(host, "/v1/officials");

        Assert.Equal(["p0", "p1", "p2", "p3", "p4", "p5"], Ids(page));
        Assert.Equal(1, page.RootElement.GetProperty("release").GetInt32());
        Assert.Contains("Votes are not counted", page.RootElement.GetProperty("recordCountScope").GetString(), StringComparison.Ordinal);
        Assert.Equal(6, page.RootElement.GetProperty("officialsInRelease").GetInt32());
        Assert.Contains("with or without a term", page.RootElement.GetProperty("officialsInReleaseScope").GetString(), StringComparison.Ordinal);
        var first = page.RootElement.GetProperty("officials")[0];
        Assert.Equal("Ann Able", first.GetProperty("name").GetString());
        Assert.Equal("senator", first.GetProperty("kind").GetString());
        Assert.Equal("Senator", first.GetProperty("office").GetString());
        Assert.Equal("MA", first.GetProperty("state").GetString());
        Assert.Equal("Democrat", first.GetProperty("party").GetString());
        Assert.Equal("2031-01-03", first.GetProperty("termEndsOn").GetString());
        Assert.Equal(2, first.GetProperty("recordCount").GetInt32());
        var noTerm = page.RootElement.GetProperty("officials")[5];
        Assert.Equal(JsonValueKind.Null, noTerm.GetProperty("kind").ValueKind);
        Assert.Equal(0, noTerm.GetProperty("recordCount").GetInt32());
        Assert.Equal(JsonValueKind.Null, noTerm.GetProperty("photoPath").ValueKind);
    }

    [Fact]
    public async Task TheOfficialRouteReturnsOnlyTheStoredPublicationPhotoPath()
    {
        await using var host = await StartAsync();
        var path = "photos/" + new string('a', 64) + ".jpg";
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"UPDATE release_officials SET photo_path = '{path}' WHERE official_id = 'p0';");

        using var page = await GetAsync(host, "/v1/officials");

        var official = page.RootElement.GetProperty("officials").EnumerateArray().First(row => row.GetProperty("officialId").GetString() == "p0");
        Assert.Equal(path, official.GetProperty("photoPath").GetString());
        Assert.Equal(JsonValueKind.Null, page.RootElement.GetProperty("officials")[5].GetProperty("photoPath").ValueKind);
    }

    [Theory]
    [InlineData("chamber=senate", "p0,p2")]
    [InlineData("chamber=house", "p1,p3")]
    [InlineData("chamber=executive", "p4")]
    [InlineData("state=tx", "p1,p2")]
    [InlineData("party=REPUBLICAN", "p1,p2,p4")]
    [InlineData("name=ben", "p1")]
    [InlineData("name=baker", "p1")]
    [InlineData("name=DOLE", "p3")]
    [InlineData("name=100", "p2")]
    [InlineData("name=aker", "")]
    [InlineData("name=ann%20a", "p0")]
    [InlineData("name=able%20ann", "")]
    [InlineData("name=cy%20100%25", "p2")]
    [InlineData("name=%25", "")]
    [InlineData("name=fox", "p5")]
    [InlineData("name=fox-g", "p5")]
    [InlineData("chamber=senate&state=MA&party=democrat&name=a", "p0")]
    public async Task EachFilterNarrowsTheListAndANamePrefixMatchesAWordStartAndTakesPercentAsLiteralText(string query, string expected)
    {
        await using var host = await StartAsync();

        using var page = await GetAsync(host, "/v1/officials?" + query);

        Assert.Equal(expected.Length == 0 ? [] : expected.Split(','), Ids(page));
    }

    [Theory]
    [InlineData("name=gray")]
    [InlineData("name=%5C")]
    public async Task AHyphenDoesNotStartAWordAndABackslashMatchesNoNameAndCausesNoError(string query)
    {
        await using var host = await StartAsync();

        using var page = await GetAsync(host, "/v1/officials?" + query);

        Assert.Empty(Ids(page));
    }

    [Fact]
    public async Task TheCountOfOfficialsInTheReleaseNamesOnlyTheActiveReleaseAndNotAnInactiveOne()
    {
        await using var host = await StartAsync();
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, """
            INSERT INTO release_officials (release_number, official_id, name) VALUES (2, 'q0', 'Gus Gray'), (2, 'q1', 'Hal Hart'), (2, 'q2', 'Ivy Iver');
            """);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));

        using var page = await GetAsync(host, "/v1/officials");

        Assert.Equal(6, page.RootElement.GetProperty("officialsInRelease").GetInt32());
        Assert.DoesNotContain("q0", Ids(page));
    }

    [Theory]
    [InlineData("limit=2")]
    [InlineData("chamber=senate")]
    [InlineData("name=zzz")]
    public async Task TheCountOfOfficialsInTheReleaseIgnoresFiltersAndPaging(string query)
    {
        await using var host = await StartAsync();

        using var page = await GetAsync(host, "/v1/officials?" + query);

        Assert.Equal(6, page.RootElement.GetProperty("officialsInRelease").GetInt32());
    }

    [Fact]
    public async Task AWalkWithTwoRowsAPageVisitsEveryOfficialOnceInOrderAndTheLastPageHasNoCursor()
    {
        await using var host = await StartAsync();
        var seen = new List<string>();
        string? cursor = null;
        do
        {
            using var page = await GetAsync(host, "/v1/officials?limit=2" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)));
            seen.AddRange(Ids(page));
            cursor = page.RootElement.GetProperty("nextCursor").GetString();
        }
        while (cursor is not null);

        Assert.Equal(["p0", "p1", "p2", "p3", "p4", "p5"], seen);
    }

    [Fact]
    public async Task AWalkOverOfficialsWithNamesAndIdsOfTheFullColumnLengthFollowsEveryCursorTheServerWrites()
    {
        await using var host = await ApiHost.StartAsync(postgres);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        string[] ids = [.. "abc".Select(letter => new string(letter, 256))];
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase, [.. ids.Select(id => new SeededOfficial(id, new string(id[0], 255) + "é"))]);
        await OfficialsSeed.AddEntriesAsync(host.OperationalDatabase, new SeededEntry("statement", 'a', ids[0]));
        var seen = new List<string>();
        string? cursor = null;
        do
        {
            using var page = await GetAsync(host, "/v1/officials?limit=1" + (cursor is null ? "" : "&cursor=" + Uri.EscapeDataString(cursor)));
            seen.AddRange(Ids(page));
            cursor = page.RootElement.GetProperty("nextCursor").GetString();
            Assert.True(cursor is null || cursor.Length > 200);
        }
        while (cursor is not null);

        Assert.Equal(ids, seen);
    }

    [Fact]
    public async Task ThePageSizeIsAtMostFiftyAndALimitAboveItIsRefused()
    {
        await using var host = await StartAsync();

        using var page = await GetAsync(host, "/v1/officials?limit=50");
        using var refused = await host.Client.GetAsync("/v1/officials?limit=51");

        Assert.Equal(6, Ids(page).Length);
        Assert.Equal(HttpStatusCode.BadRequest, refused.StatusCode);
    }

    [Theory]
    [InlineData("chamber=court")]
    [InlineData("chamber=house&chamber=senate")]
    [InlineData("limit=0")]
    [InlineData("cursor=nonsense")]
    [InlineData("cursor=o.1.!!!")]
    public async Task AnInvalidParameterAnswers400(string query)
    {
        await using var host = await StartAsync();

        using var response = await host.Client.GetAsync("/v1/officials?" + query);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task ACursorOfAnOlderReleaseAnswers409WithTheActiveRelease()
    {
        await using var host = await StartAsync();
        string cursor;
        using (var page = await GetAsync(host, "/v1/officials?limit=2")) cursor = page.RootElement.GetProperty("nextCursor").GetString()!;
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));

        using var problem = await GetAsync(host, "/v1/officials?limit=2&cursor=" + Uri.EscapeDataString(cursor), HttpStatusCode.Conflict);

        Assert.Equal(2, problem.RootElement.GetProperty("release").GetInt32());
    }

    [Fact]
    public async Task WithNoActiveReleaseTheListAnswers503AndALookupAnswers404()
    {
        await using var host = await ApiHost.StartAsync(postgres);

        Assert.Equal(HttpStatusCode.ServiceUnavailable, (await host.Client.GetAsync("/v1/officials")).StatusCode);
        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/v1/officials/p0")).StatusCode);
    }

    [Fact]
    public async Task OneOfficialShowsTheTermsAndEveryFigureWithItsBaseAndItsScope()
    {
        await using var host = await StartAsync(withVotesAndBills: true);

        using var detail = await GetAsync(host, "/v1/officials/p0");
        var root = detail.RootElement;

        Assert.Equal(119, root.GetProperty("congress").GetInt32());
        Assert.Equal("Ann Able", root.GetProperty("official").GetProperty("name").GetString());
        Assert.Equal(2, root.GetProperty("official").GetProperty("recordCount").GetInt32());
        Assert.Equal("Senator", root.GetProperty("terms")[0].GetProperty("office").GetString());
        var missed = root.GetProperty("missedVotes").EnumerateArray().Select(row => $"{row.GetProperty("chamber").GetString()} {row.GetProperty("missed").GetInt32()}/{row.GetProperty("held").GetInt32()}").ToArray();
        Assert.Equal(["house 0/2", "senate 0/1"], missed);
        Assert.Contains("Congress 119", root.GetProperty("missedVotes")[0].GetProperty("scope").GetString(), StringComparison.Ordinal);
        var bills = root.GetProperty("bills");
        Assert.Equal([2, 0, 2], [bills.GetProperty("sponsored").GetInt32(), bills.GetProperty("cosponsored").GetInt32(), bills.GetProperty("billsInCongress").GetInt32()]);
        Assert.Contains("Congress 119", bills.GetProperty("scope").GetString(), StringComparison.Ordinal);
        Assert.Contains("counts once, as sponsored", bills.GetProperty("scope").GetString(), StringComparison.Ordinal);
        Assert.Contains("Congress of its bill number", bills.GetProperty("scope").GetString(), StringComparison.Ordinal);
        Assert.Contains("Statements are not counted.", root.GetProperty("issues").GetProperty("scope").GetString(), StringComparison.Ordinal);
        Assert.Contains("have an entry row", root.GetProperty("recordCountScope").GetString(), StringComparison.Ordinal);
        var issues = root.GetProperty("issues").GetProperty("issues").EnumerateArray().Select(row => $"{row.GetProperty("issueId").GetString()} {row.GetProperty("sponsoredBills").GetInt32()} {row.GetProperty("votesCast").GetInt32()}").ToArray();
        Assert.Equal(["health 2 1", "tax 1 1"], issues);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("executiveActions").ValueKind);
    }

    [Fact]
    public async Task DetailKeepsBothChambersAtTheNewestAttendanceCongressAndOmitsOlderCongresses()
    {
        await using var host = await StartAsync();
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase,
            new SeededVote('a', "2026-01-01") { Congress = 118, Casts = ["Not Voting"] },
            new SeededVote('b', "2026-02-01") { Congress = 120, Casts = ["Not Voting"] },
            new SeededVote('c', "2026-02-02") { Congress = 120, Chamber = "Senate", Casts = ["Yea"] });

        using var detail = await GetAsync(host, "/v1/officials/p0");

        Assert.Equal(120, detail.RootElement.GetProperty("congress").GetInt32());
        var figures = detail.RootElement.GetProperty("missedVotes");
        Assert.Equal(["house", "senate"], figures.EnumerateArray().Select(figure => figure.GetProperty("chamber").GetString()));
        Assert.All(figures.EnumerateArray(), figure => Assert.Equal(120, figure.GetProperty("congress").GetInt32()));
        Assert.Equal(1, figures[0].GetProperty("missed").GetInt32());
        Assert.Empty(detail.RootElement.GetProperty("partyLines").EnumerateArray());
    }

    [Fact]
    public async Task MissedVotesAndCosponsoredBillsFollowTheCastsAndTheWithdrawnCosponsorshipsOfTheNewestCongress()
    {
        await using var host = await StartAsync(withVotesAndBills: true);

        using var detail = await GetAsync(host, "/v1/officials/p1");
        var root = detail.RootElement;

        Assert.Equal(["house 1/2", "senate 0/1"], root.GetProperty("missedVotes").EnumerateArray().Select(row => $"{row.GetProperty("chamber").GetString()} {row.GetProperty("missed").GetInt32()}/{row.GetProperty("held").GetInt32()}"));
        Assert.Equal(2, root.GetProperty("bills").GetProperty("cosponsored").GetInt32());
        using var other = await GetAsync(host, "/v1/officials/p3");
        Assert.Equal(["house 0/2", "senate 1/1"], other.RootElement.GetProperty("missedVotes").EnumerateArray().Select(row => $"{row.GetProperty("chamber").GetString()} {row.GetProperty("missed").GetInt32()}/{row.GetProperty("held").GetInt32()}"));
        Assert.Equal(0, other.RootElement.GetProperty("bills").GetProperty("cosponsored").GetInt32());
    }

    [Fact]
    public async Task PartyLinesUsePinnedPartiesAndOnlyCastSubstantiveSplitVotesOfTheAttendanceCongress()
    {
        await using var host = await StartAsync(withVotesAndBills: true);
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, new SeededVote('e', "2026-02-07")
        {
            Casts = ["Yea", "Not Voting", "Yea", "Present", "Nay"],
            RollCall = 5
        }, new SeededVote('f', "2026-02-08")
        {
            Casts = ["Yea", "Not Voting", "No", "Present", "Nay"],
            RollCall = 6
        });
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            SET session_replication_role = replica;
            UPDATE vote_member_list_entries SET party = CASE person_id
                WHEN 'p0' THEN 'Democrat' WHEN 'p1' THEN 'Republican' WHEN 'p2' THEN 'Republican' WHEN 'p3' THEN 'Democrat' ELSE 'Unknown' END;
            UPDATE vote_member_list_entries SET party = 'Democrat' WHERE list_id IN ('{new string('e', 64)}', '{new string('f', 64)}') AND person_id = 'p2';
            UPDATE vote_revisions SET columns_written = true, alignment = 'Split', democrat_majority = 'Yea', republican_majority = 'Nay'
              WHERE record_id IN ('{new string('a', 64)}', '{new string('c', 64)}', '{new string('d', 64)}', '{new string('e', 64)}', '{new string('f', 64)}');
            """);

        using var detail = await GetAsync(host, "/v1/officials/p1");
        var root = detail.RootElement;

        var partyLines = root.GetProperty("partyLines").EnumerateArray().ToArray();
        Assert.Single(partyLines);
        Assert.Equal("senate", partyLines[0].GetProperty("chamber").GetString());
        Assert.Equal("Republican", partyLines[0].GetProperty("party").GetString());
        Assert.Equal([0, 1, 1], [partyLines[0].GetProperty("withParty").GetInt32(), partyLines[0].GetProperty("againstParty").GetInt32(), partyLines[0].GetProperty("cast").GetInt32()]);
        Assert.Equal("2026-02-05", partyLines[0].GetProperty("firstVotedOn").GetString());
        Assert.Contains("Congress 119", partyLines[0].GetProperty("scope").GetString(), StringComparison.Ordinal);
        using var switched = await GetAsync(host, "/v1/officials/p2");
        var switchedLines = switched.RootElement.GetProperty("partyLines").EnumerateArray().ToArray();
        PublishedOfficialVote[] expectedVotes =
        [
            PartyVote('a', PublishedChamber.House, 119, "2026-02-01", "Nay", "Republican", "Nay"),
            PartyVote('c', PublishedChamber.Senate, 119, "2026-02-05", "Yea", "Republican", "Nay"),
            PartyVote('d', PublishedChamber.House, 118, "2024-05-01", "Yea", "Republican", "Nay"),
            PartyVote('e', PublishedChamber.House, 119, "2026-02-07", "Yea", "Democrat", "Yea"),
            PartyVote('f', PublishedChamber.House, 119, "2026-02-08", "No", "Democrat", "Yea")
        ];
        var expectedPartyLines = VotesWithParty.Of(expectedVotes.Where(vote => vote.Congress == 119));
        Assert.Equal(expectedPartyLines.Select(row => $"{row.Chamber.ToString().ToLowerInvariant()} {row.Party} {row.Matched} {row.Breaks.Length} {row.Cast}"), switchedLines.Select(row =>
            $"{row.GetProperty("chamber").GetString()} {row.GetProperty("party").GetString()} {row.GetProperty("withParty").GetInt32()} {row.GetProperty("againstParty").GetInt32()} {row.GetProperty("cast").GetInt32()}"));
        Assert.Equal(["2026-02-07", "2026-02-01", "2026-02-05"], switchedLines.Select(row => row.GetProperty("firstVotedOn").GetString()));
        Assert.Equal(["2026-02-08", "2026-02-01", "2026-02-05"], switchedLines.Select(row => row.GetProperty("lastVotedOn").GetString()));
        using var other = await GetAsync(host, "/v1/officials/p3");
        Assert.Empty(other.RootElement.GetProperty("partyLines").EnumerateArray());
    }

    [Theory]
    [InlineData("p0", "2 0 2")]
    [InlineData("p1", "0 2 2")]
    [InlineData("p3", "0 0 2")]
    public async Task StoredBillFiguresEqualTheCountsOfTheSeededBillsBecauseAWithdrawnCosponsorAndAMissingMemberRowCountZero(string official, string expected)
    {
        await using var host = await StartAsync(withVotesAndBills: true);

        using var detail = await GetAsync(host, $"/v1/officials/{official}");
        var bills = detail.RootElement.GetProperty("bills");

        Assert.Equal(119, bills.GetProperty("congress").GetInt32());
        Assert.Equal(expected, $"{bills.GetProperty("sponsored").GetInt32()} {bills.GetProperty("cosponsored").GetInt32()} {bills.GetProperty("billsInCongress").GetInt32()}");
    }

    [Fact]
    public async Task TheOfficialRouteServesTheStoredBillRowsAndNotARecount()
    {
        await using var host = await StartAsync(withVotesAndBills: true);
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, """
            UPDATE release_bill_counts SET bills = 40 WHERE congress = 119;
            UPDATE release_official_bill_counts SET sponsored = 9 WHERE official_id = 'p0' AND congress = 119;
            """);

        using var detail = await GetAsync(host, "/v1/officials/p0");
        var bills = detail.RootElement.GetProperty("bills");

        Assert.Equal([9, 40], [bills.GetProperty("sponsored").GetInt32(), bills.GetProperty("billsInCongress").GetInt32()]);
    }

    [Fact]
    public async Task AReleaseWithNoStoredBillRowShowsTheBillFigureAsAbsentAndNeverAsZeroWhileTheIssueFiguresStay()
    {
        await using var host = await StartAsync(withVotesAndBills: true, storeBillCounts: false);

        using var detail = await GetAsync(host, "/v1/officials/p0");

        Assert.Equal(JsonValueKind.Null, detail.RootElement.GetProperty("bills").ValueKind);
        Assert.NotEmpty(detail.RootElement.GetProperty("issues").GetProperty("issues").EnumerateArray());
    }

    [Fact]
    public async Task AnExecutiveOfficialShowsTheExecutiveActionsByDocumentTypeWithTheBaseOfTheRelease()
    {
        await using var host = await StartAsync(withVotesAndBills: true);

        using var detail = await GetAsync(host, "/v1/officials/p4");
        var root = detail.RootElement;
        var executive = root.GetProperty("executiveActions");

        Assert.Equal(["ExecutiveOrder 2", "Proclamation 1"], executive.GetProperty("byDocumentType").EnumerateArray().Select(row => $"{row.GetProperty("documentType").GetString()} {row.GetProperty("actions").GetInt32()}"));
        Assert.Equal(3, executive.GetProperty("actionsInRelease").GetInt32());
        Assert.Contains("name the official", executive.GetProperty("scope").GetString(), StringComparison.Ordinal);
    }

    [Fact]
    public async Task AnExecutiveWithoutAttendanceDoesNotGetLegislativeFiguresFromTheReleaseDate()
    {
        await using var host = await StartAsync();

        using var detail = await GetAsync(host, "/v1/officials/p4");

        Assert.Equal(JsonValueKind.Null, detail.RootElement.GetProperty("congress").ValueKind);
        Assert.Equal(JsonValueKind.Null, detail.RootElement.GetProperty("bills").ValueKind);
        Assert.Equal(JsonValueKind.Null, detail.RootElement.GetProperty("issues").ValueKind);
    }

    [Fact]
    public async Task AnOfficialWithNoVoteShowsNoCongressAndNoBillOrIssueFigure()
    {
        await using var host = await StartAsync();

        using var detail = await GetAsync(host, "/v1/officials/p5");
        var root = detail.RootElement;

        Assert.Equal(JsonValueKind.Null, root.GetProperty("congress").ValueKind);
        Assert.Empty(root.GetProperty("missedVotes").EnumerateArray());
        Assert.Equal(JsonValueKind.Null, root.GetProperty("bills").ValueKind);
        Assert.Equal(JsonValueKind.Null, root.GetProperty("issues").ValueKind);
        Assert.Empty(root.GetProperty("terms").EnumerateArray());
    }

    [Theory]
    [InlineData("2025-01-03T00:01:00+06:00", 118)]
    [InlineData("2025-01-02T23:59:00-06:00", 119)]
    [InlineData("1934-12-31T23:59:00Z", null)]
    public async Task MemberWithoutAttendanceUsesTheReleaseUtcDateForBillAndIssueFigures(string publishedAt, int? expectedCongress)
    {
        await using var host = await ApiHost.StartAsync(postgres);
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, DateTimeOffset.Parse(publishedAt));
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase,
            new SeededOfficial("p5", "Fay Fox", new SeededTerm("Representative", "Representative", "2023-01-03") { State = "CA", District = 1, Party = "Democrat" }));
        await BillsSeed.AddBillsAsync(host.OperationalDatabase,
            new SeededBill('f', "2025-01-02") { Congress = 118, BillNumber = 6, Issues = ["health"], Sponsorships = [new("p5", "Sponsor", "2025-01-02")] },
            new SeededBill('9', "2025-01-03") { Congress = 119, BillNumber = 7, Issues = ["tax"], Sponsorships = [new("p5", "Sponsor", "2025-01-03")] });
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, "INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (1, 'health', 'Health policy', 'Health'), (1, 'tax', 'Tax policy', 'Tax');");
        if (expectedCongress is { } storedCongress) await StoredBillCounts.FillAsync(host.OperationalDatabase, 1, storedCongress);

        using var detail = await GetAsync(host, "/v1/officials/p5");
        var root = detail.RootElement;

        Assert.Equal(JsonValueKind.Null, root.GetProperty("congress").ValueKind);
        Assert.Empty(root.GetProperty("missedVotes").EnumerateArray());
        Assert.Empty(root.GetProperty("partyLines").EnumerateArray());
        if (expectedCongress is not { } congress)
        {
            Assert.Equal(JsonValueKind.Null, root.GetProperty("bills").ValueKind);
            Assert.Equal(JsonValueKind.Null, root.GetProperty("issues").ValueKind);
            return;
        }
        Assert.Equal(congress, root.GetProperty("bills").GetProperty("congress").GetInt32());
        Assert.Equal(1, root.GetProperty("bills").GetProperty("sponsored").GetInt32());
        var issue = Assert.Single(root.GetProperty("issues").GetProperty("issues").EnumerateArray());
        Assert.Equal(congress == 118 ? "health" : "tax", issue.GetProperty("issueId").GetString());
        Assert.Equal(congress == 118 ? "Health policy" : "Tax policy", issue.GetProperty("name").GetString());
    }

    [Fact]
    public async Task AnUnknownOfficialAnswers404AndNoAnswerHoldsAPersonOrBioguideId()
    {
        await using var host = await StartAsync(withVotesAndBills: true);

        Assert.Equal(HttpStatusCode.NotFound, (await host.Client.GetAsync("/v1/officials/nobody")).StatusCode);
        foreach (var path in new[] { "/v1/officials", "/v1/officials/p0", "/v1/officials/p4" })
        {
            var body = await host.Client.GetStringAsync(path);
            Assert.DoesNotContain("bioguide", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("personId", body, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("B000", body, StringComparison.Ordinal);
        }
    }
}
