using System.Net;
using System.Text.Json;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Officials;
using CivicLens.Tests.PublicApi.Votes;

namespace CivicLens.Tests.PublicApi.Boards;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class BoardEndpointsTests(PostgresCollection postgres)
{
    [Fact]
    public async Task OneResponseKeepsAllFourBoardsOrderedAndUsesTheSelectedCongressAndPinnedParty()
    {
        var house118 = Enumerable.Range(0, 25).Select(index => Vote((char)('a' + index), "House", 118, index + 1,
            ["p0", "p1"], ["Yea", "Nay"], "Split")).ToList();
        house118.Add(Vote('0', "House", 118, 26, ["p2", "p7", "p1"], ["Yea", "Yea", "Yea"], "PartiesAgree"));
        var allVotes = house118.Concat(new[]
        {
            Vote('1', "House", 119, 1, ["p0", "p1"], ["Yea", "Nay"], "Split"),
            Vote('2', "Senate", 119, 1, ["p2", "p4"], ["Yea", "Yea"], "PartiesAgree"),
            Vote('3', "Senate", 118, 1, ["p3", "p5"], ["Yea", "Nay"], "Split")
        }.Concat(Enumerable.Range(0, 25).Select(index => Vote((char)('A' + index), "Senate", 119, index + 2,
            ["p2", "p4"], ["Yea", "Nay"], "Split") with { RecordId = (index + 4).ToString("x64") }))).ToArray();
        await using var host = await StartAsync(allVotes);

        using var answer = await GetAsync(host);
        var root = answer.RootElement;
        Assert.Equal(1, root.GetProperty("release").GetInt32());
        var boards = root.GetProperty("boards").EnumerateArray().ToArray();
        Assert.Collection(boards,
            board => AssertBoardIdentity(board, "house", "Democrat"),
            board => AssertBoardIdentity(board, "house", "Republican"),
            board => AssertBoardIdentity(board, "senate", "Democrat"),
            board => AssertBoardIdentity(board, "senate", "Republican"));

        var houseDemocrats = Board(boards, "house", "Democrat");
        Assert.Equal((118, 119, 1), (houseDemocrats.GetProperty("congress").GetInt32(),
            houseDemocrats.GetProperty("newerCongress").GetInt32(), houseDemocrats.GetProperty("withoutVotes").GetInt32()));
        Assert.Equal(["p0"], Ids(houseDemocrats, "loyalists"));
        Assert.Equal(["p2", "p7"], Ids(houseDemocrats, "tooFewSplitVotes"));
        Assert.Equal(25, Member(houseDemocrats, "p0").GetProperty("cast").GetInt32());
        Assert.Equal(0, Member(houseDemocrats, "p2").GetProperty("cast").GetInt32());
        Assert.Equal("Delegate", Member(houseDemocrats, "p7").GetProperty("terms")[0].GetProperty("houseOffice").GetString());
        var p2Terms = Member(houseDemocrats, "p2").GetProperty("terms").EnumerateArray().ToArray();
        Assert.Equal(2, p2Terms.Length);
        Assert.Equal(("DC", "2023-01-03", "2024-01-02"),
            (p2Terms[0].GetProperty("state").GetString(), p2Terms[0].GetProperty("startsOn").GetString(), p2Terms[0].GetProperty("endsOn").GetString()));
        Assert.Equal(("NY", "2024-01-03", "2025-01-02"),
            (p2Terms[1].GetProperty("state").GetString(), p2Terms[1].GetProperty("startsOn").GetString(), p2Terms[1].GetProperty("endsOn").GetString()));
        Assert.DoesNotContain(p2Terms, term => term.GetProperty("state").GetString() == "TX");
        Assert.Equal("p0", Member(houseDemocrats, "p0").GetProperty("officialId").GetString());
        Assert.Contains("Congress 118", Member(houseDemocrats, "p0").GetProperty("partyLineScope").GetString(), StringComparison.Ordinal);

        var senateDemocrats = Board(boards, "senate", "Democrat");
        Assert.Equal(119, senateDemocrats.GetProperty("congress").GetInt32());
        Assert.Equal(JsonValueKind.Null, senateDemocrats.GetProperty("newerCongress").ValueKind);
        Assert.Equal(2, senateDemocrats.GetProperty("notInCongress").GetInt32());
        Assert.Equal(["p2"], Ids(senateDemocrats, "loyalists"));
        Assert.Equal(("Senate", "CA"), (Member(senateDemocrats, "p2").GetProperty("terms")[0].GetProperty("office").GetString(),
            Member(senateDemocrats, "p2").GetProperty("terms")[0].GetProperty("state").GetString()));

        var agreements = root.GetProperty("agreements").EnumerateArray().ToArray();
        Assert.Equal(2, agreements.Length);
        var houseAgreement = agreements.Single(item => item.GetProperty("chamber").GetString() == "house").GetProperty("agreement");
        Assert.Equal(118, houseAgreement.GetProperty("congress").GetInt32());
        Assert.Equal(25, houseAgreement.GetProperty("split").GetInt32());
        Assert.Equal(1, houseAgreement.GetProperty("agreed").GetInt32());
        var senateAgreement = agreements.Single(item => item.GetProperty("chamber").GetString() == "senate").GetProperty("agreement");
        Assert.Equal(119, senateAgreement.GetProperty("congress").GetInt32());
        Assert.Equal(25, senateAgreement.GetProperty("split").GetInt32());
    }

    [Fact]
    public async Task AValidEmptyReleaseReturnsFourKnownEmptyBoardsAndTwoExplicitNullAgreements()
    {
        await using var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero), 0);

        using var answer = await GetAsync(host);

        var boards = answer.RootElement.GetProperty("boards").EnumerateArray().ToArray();
        Assert.Equal(4, boards.Length);
        Assert.All(boards, board =>
        {
            Assert.Empty(board.GetProperty("members").EnumerateArray());
            Assert.Empty(board.GetProperty("breakers").EnumerateArray());
            Assert.Equal(0, board.GetProperty("withoutVotes").GetInt32());
        });
        var agreements = answer.RootElement.GetProperty("agreements").EnumerateArray().ToArray();
        Assert.Equal(["house", "senate"], agreements.Select(item => item.GetProperty("chamber").GetString()!).ToArray());
        Assert.All(agreements, item => Assert.Equal(JsonValueKind.Null, item.GetProperty("agreement").ValueKind));
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public async Task MissingPartyPinOrUnwrittenAlignmentReturnsUnknownBoardsAndAgreements(bool pinParties, bool writeAlignment)
    {
        var vote = Vote('a', "House", 119, 1, ["p0", "p1"], ["Yea", "Nay"], "Split");
        await using var host = await StartAsync([vote], pinParties, writeAlignment);

        using var answer = await GetAsync(host);

        Assert.All(answer.RootElement.GetProperty("boards").EnumerateArray(), board =>
        {
            Assert.Equal(JsonValueKind.Null, board.GetProperty("members").ValueKind);
            Assert.Equal(JsonValueKind.Null, board.GetProperty("breakers").ValueKind);
            Assert.Equal(JsonValueKind.Null, board.GetProperty("withoutVotes").ValueKind);
        });
        Assert.All(answer.RootElement.GetProperty("agreements").EnumerateArray(), item =>
            Assert.Equal(JsonValueKind.Null, item.GetProperty("agreement").ValueKind));
    }

    [Fact]
    public async Task MissingTypedVoteCongressReturnsUnknownBoardsAndAgreements()
    {
        var vote = Vote('a', "House", 119, 1, ["p0", "p1"], ["Yea", "Nay"], "Split");
        await using var host = await StartAsync([vote]);
        await ViewsSeed.ExecuteAsync(host.OperationalDatabase, $"""
            SET session_replication_role = replica;
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number)
            VALUES ('vote', '{new string('f', 64)}', 1, 1);
            """);

        using var answer = await GetAsync(host);

        Assert.All(answer.RootElement.GetProperty("boards").EnumerateArray(), board =>
            Assert.Equal(JsonValueKind.Null, board.GetProperty("members").ValueKind));
        Assert.All(answer.RootElement.GetProperty("agreements").EnumerateArray(), item =>
            Assert.Equal(JsonValueKind.Null, item.GetProperty("agreement").ValueKind));
    }

    [Fact]
    public async Task NoActiveReleaseAnswersServiceUnavailable()
    {
        await using var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());

        using var response = await host.Client.GetAsync("/v1/boards");

        Assert.Equal(HttpStatusCode.ServiceUnavailable, response.StatusCode);
    }

    private async Task<ApiHost> StartAsync(BoardVote[] votes, bool pinParties = true, bool writeAlignment = true)
    {
        var host = await ApiHost.StartAsync(postgres, new FakeReleaseSite());
        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await OfficialsSeed.AddOfficialsAsync(host.OperationalDatabase,
            new("p0", "Ada Able", new SeededTerm("House", "Representative", "2021-01-03") { State = "NY", District = 1, Party = "Democrat" }),
            new("p1", "Ben Baker", new SeededTerm("House", "Representative", "2021-01-03") { State = "CA", District = 1, Party = "Republican" }),
            new("p2", "Cal Cruz",
                new SeededTerm("House", "Representative", "2021-01-03") { State = "DC", District = 0, Party = "Democrat", EndsOn = "2023-01-02" },
                new SeededTerm("House", "Representative", "2023-01-03") { State = "DC", District = 0, Party = "Democrat", EndsOn = "2024-01-02" },
                new SeededTerm("House", "Representative", "2024-01-03") { State = "NY", District = 1, Party = "Democrat", EndsOn = "2025-01-02" },
                new SeededTerm("House", "Representative", "2025-01-03") { State = "TX", District = 1, Party = "Republican" },
                new SeededTerm("Senate", "Senator", "2025-01-03") { State = "CA", Party = "Democrat" }),
            new("p3", "Dee Dole", new SeededTerm("Senate", "Senator", "2021-01-03") { State = "NY", Party = "Democrat" }),
            new("p4", "Eli Ellis", new SeededTerm("Senate", "Senator", "2021-01-03") { State = "TX", Party = "Republican" }),
            new("p5", "Fox Finn", new SeededTerm("Senate", "Senator", "2021-01-03") { State = "FL", Party = "Republican" }),
            new("p6", "Gia Gray", new SeededTerm("House", "Representative", "2021-01-03") { State = "IL", District = 1, Party = "Democrat" }),
            new("p7", "Hal Hale", new SeededTerm("House", "Representative", "2021-01-03") { State = "DC", District = 0, Party = "Democrat" }));
        await ViewsSeed.AddVotesAsync(host.OperationalDatabase, [.. votes.Select(vote => vote.ToViewVote())]);
        if (pinParties)
            await ViewsSeed.ExecuteAsync(host.OperationalDatabase, """
                UPDATE vote_member_list_entries SET party = CASE person_id
                    WHEN 'p0' THEN 'Democrat' WHEN 'p1' THEN 'Republican' WHEN 'p2' THEN 'Democrat'
                    WHEN 'p3' THEN 'Democrat' WHEN 'p4' THEN 'Republican' WHEN 'p5' THEN 'Republican'
                    WHEN 'p7' THEN 'Democrat' ELSE 'Unknown' END;
                """);
        if (writeAlignment)
            foreach (var vote in votes)
                await ViewsSeed.ExecuteAsync(host.OperationalDatabase,
                    $"UPDATE vote_revisions SET columns_written = true, alignment = '{vote.Alignment}', democrat_majority = '{vote.DemocratMajority}', republican_majority = '{vote.RepublicanMajority}' WHERE record_id = '{vote.RecordId}'");
        return host;
    }

    private static BoardVote Vote(char id, string chamber, int congress, int rollCall, string[] people, string[] casts, string alignment) =>
        new(id, congress == 118 ? "2023-02-01" : "2025-02-01")
        {
            Chamber = chamber,
            Congress = congress,
            RollCall = rollCall,
            PersonIds = people,
            Casts = casts,
            Alignment = alignment,
            DemocratMajority = "Yea",
            RepublicanMajority = alignment == "Split" ? "Nay" : "Yea"
        };

    private static JsonElement Board(JsonElement[] boards, string chamber, string party) => boards.Single(board =>
        board.GetProperty("board").GetProperty("chamber").GetString() == chamber &&
        board.GetProperty("board").GetProperty("party").GetString() == party);

    private static void AssertBoardIdentity(JsonElement board, string chamber, string party)
    {
        Assert.Equal(chamber, board.GetProperty("board").GetProperty("chamber").GetString());
        Assert.Equal(party, board.GetProperty("board").GetProperty("party").GetString());
    }

    private static JsonElement Member(JsonElement board, string officialId) => board.GetProperty("members").EnumerateArray()
        .Single(member => member.GetProperty("officialId").GetString() == officialId);

    private static string[] Ids(JsonElement board, string property) => board.GetProperty(property).EnumerateArray()
        .Select(item => item.GetString()!).ToArray();

    private static async Task<JsonDocument> GetAsync(ApiHost host)
    {
        using var response = await host.Client.GetAsync("/v1/boards");
        var body = await response.Content.ReadAsStringAsync();
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return JsonDocument.Parse(body);
    }

    private sealed record BoardVote(char Id, string VotedOn)
    {
        public string Chamber { get; init; } = "House";
        public int Congress { get; init; }
        public int RollCall { get; init; }
        public string[] PersonIds { get; init; } = [];
        public string[] Casts { get; init; } = [];
        public string Alignment { get; init; } = "Split";
        public string DemocratMajority { get; init; } = "Yea";
        public string RepublicanMajority { get; init; } = "Nay";
        public string RecordId { get; init; } = ((int)Id).ToString("x64");
        public CivicLens.Tests.PublicApi.Votes.SeededVote ToViewVote() => new(Id, VotedOn)
        {
            RecordId = RecordId,
            Chamber = Chamber,
            Congress = Congress,
            RollCall = RollCall,
            PersonIds = PersonIds,
            Casts = Casts
        };
    }
}
