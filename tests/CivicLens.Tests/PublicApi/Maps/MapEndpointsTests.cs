using System.Net;
using System.Net.Http.Json;
using CivicLens.PublicApi.Maps;
using CivicLens.PublicApi.Records;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Officials;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CivicLens.Tests.PublicApi.Maps;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class MapEndpointsTests(PostgresCollection postgres)
{
    private static readonly DateTimeOffset ReleaseDay = new(2026, 6, 1, 0, 0, 0, TimeSpan.Zero);
    private const string IssuePath = "/v1/maps?layer=issueActivity&issue=zz-target";

    [Fact]
    public async Task AHistoricalMapClampsToTheLastDayAndTheSuccessorGetsOnlyTheirOwnAttendance()
    {
        await using var host = await StartAsync();
        var database = host.OperationalDatabase;
        await OfficialsSeed.AddOfficialsAsync(database,
            new("predecessor", "First Holder", House("2023-01-03", "AL", 0) with { EndsOn = "2025-01-02" }),
            new("successor", "Next Holder", House("2025-01-03", "AL", 0) with { Party = "Democrat", EndsOn = "2026-06-01" },
                House("2026-06-01", "AL", 0) with { Party = "Independent", EndsOn = "2027-01-03" }));
        await ViewsSeed.AddVotesAsync(database,
            new SeededVote('a', "2024-05-11") { Congress = 118, PersonIds = ["predecessor"], Casts = ["Not Voting"] },
            new SeededVote('b', "2025-04-03") { PersonIds = ["successor"], Casts = ["Yea"] },
            new SeededVote('c', "2025-05-03") { Chamber = "Senate", PersonIds = ["successor"], Casts = ["Not Voting"] });

        var old = await ReadAsync(host, "/v1/maps?congress=118");
        Assert.Equal(new DateOnly(2025, 1, 2), old.AsOf);
        Assert.Equal(new MapSeatAnswer("AL", 0, null, "predecessor"), Assert.Single(old.Seats));
        Assert.Equal((1, 1), Attendance(Assert.Single(old.Members)));

        var current = await ReadAsync(host, "/v1/maps");
        Assert.Equal((1, 119, PublishedChamber.House, MapLayer.Attendance), (current.Release, current.Congress, current.Chamber, current.Layer));
        Assert.Equal(new DateOnly(2026, 6, 1), current.AsOf);
        Assert.Equal(new MapSeatAnswer("AL", 0, null, "successor"), Assert.Single(current.Seats));
        var successor = Assert.Single(current.Members);
        Assert.Equal("Independent", successor.Party);
        Assert.Equal(new DateOnly(2026, 6, 1), Assert.Single(successor.Terms).Start);
        Assert.Equal((1, 0), Attendance(successor));
        Assert.Equal(PublicationProtocol.OfficialPagePath("successor"), successor.PagePath);
    }

    [Fact]
    public async Task TermEndsRetainSavedDatesAndNamedMembersWithoutGuessingAmbiguousOrLegacySenateSeats()
    {
        await using var host = await StartAsync();
        var database = host.OperationalDatabase;
        await OfficialsSeed.AddOfficialsAsync(database,
            new("duplicates", "Same Official", Senate("WA"), Senate("WA") with { Party = "Independent", EndsOn = "2029-01-03" }),
            new("other", "Competing Holder", Senate("OR")),
            new("rival", "Another Competing Holder", Senate("OR")),
            new("legacy", "No Saved Class", Senate("MA")),
            new("tied", "Conflicting Seats", Senate("NY"), Senate("CA")));
        await ViewsSeed.ExecuteAsync(database, """
            UPDATE release_official_terms SET senate_class = 1 WHERE official_id IN ('duplicates', 'other', 'rival', 'tied');
            """);
        await ViewsSeed.AddVotesAsync(database, new SeededVote('a', "2025-05-03")
        {
            Chamber = "Senate",
            PersonIds = ["legacy"],
            Casts = ["Yea"]
        });

        var answer = await ReadAsync(host, "/v1/maps?chamber=senate&layer=termEnds");
        Assert.Equal(5, answer.Members.Length);
        Assert.Equal([new MapSeatAnswer("OR", null, 1, null), new MapSeatAnswer("WA", null, 1, "duplicates")], answer.Seats);
        var duplicate = Member(answer, "duplicates");
        Assert.Equal(2, duplicate.Terms.Length);
        Assert.Null(duplicate.Party);
        Assert.Null(duplicate.TermEndsOn);
        Assert.Equal(new DateOnly(2031, 1, 3), Member(answer, "legacy").TermEndsOn);
        Assert.All(answer.Members, member => Assert.Null(member.Attendance));

        var attendance = await ReadAsync(host, "/v1/maps?chamber=senate");
        Assert.Equal((1, 0), Attendance(Member(attendance, "legacy")));
        Assert.DoesNotContain(attendance.Seats, seat => seat.State == "MA");
    }

    [Fact]
    public async Task IssueActivityUsesTheSelectedIssueAndWholePersonBasesWithoutCountingCosponsorshipOrNonCasts()
    {
        await using var host = await StartAsync();
        var database = host.OperationalDatabase;
        await OfficialsSeed.AddOfficialsAsync(database,
            new("transfer", "Served In Both", House("2025-01-03", "AL", 1)),
            new("zero-votes", "Observed No Casts", House("2025-01-03", "AL", 2)),
            new("unobserved", "No Member Rows", House("2025-01-03", "AL", 3)));

        string[] issues = [.. Enumerable.Range(0, 10).Select(index => $"issue-{index}"), "zz-target"];
        await ViewsSeed.ExecuteAsync(database, "INSERT INTO release_issues (release_number, issue_id, name) VALUES (1, 'zz-target', 'Target issue')");
        await BillsSeed.AddBillsAsync(database,
            new SeededBill('d', "2025-04-01") { Issues = issues, Sponsorships = [new("transfer", "Sponsor", "2025-04-01"), new("zero-votes", "Cosponsor", "2025-04-01")] },
            new SeededBill('e', "2025-05-01") { BillNumber = 8, Sponsorships = [new("transfer", "Sponsor", "2025-05-01")] });
        await ViewsSeed.AddVotesAsync(database,
            new SeededVote('a', "2025-03-01") { PersonIds = ["transfer", "zero-votes"], Casts = ["Yea", "Not Voting"] },
            new SeededVote('b', "2025-04-01") { Chamber = "Senate", PersonIds = ["transfer"], Casts = ["Nay"], Issues = issues },
            new SeededVote('c', "2025-05-01") { Kind = "Rule", RollCall = 3, PersonIds = ["transfer", "zero-votes"], Casts = ["Yea", "Yea"], Issues = ["zz-target"] });
        await StoredBillCounts.FillAsync(database, 1, 119, onlyStoredRevisions: true);

        var answer = await ReadAsync(host, IssuePath);
        Assert.Equal(new MapIssueAnswer("zz-target", "Target issue"), answer.Issue);
        Assert.Equal(new MapIssueActivityAnswer(new(1, 2), new(1, 2)), Member(answer, "transfer").IssueActivity);
        Assert.Equal(new MapIssueActivityAnswer(new(0, 0), new(0, 0)), Member(answer, "zero-votes").IssueActivity);
        Assert.Equal(new MapIssueActivityAnswer(new(0, 0), null), Member(answer, "unobserved").IssueActivity);
        Assert.All(answer.Members, member => Assert.Null(member.Attendance));

        await ViewsSeed.ExecuteAsync(database, """
            DELETE FROM release_official_bill_counts WHERE release_number = 1 AND official_id = 'transfer';
            """);
        var inconsistentBase = await ReadAsync(host, IssuePath);
        Assert.Null(Member(inconsistentBase, "transfer").IssueActivity!.SponsoredBills);

        await ViewsSeed.ExecuteAsync(database, """
            DELETE FROM release_official_bill_counts WHERE release_number = 1;
            DELETE FROM release_bill_counts WHERE release_number = 1;
            """);
        var beforeBillSnapshot = await ReadAsync(host, IssuePath);
        Assert.Equal(new MapIssueActivityAnswer(null, new(1, 2)), Member(beforeBillSnapshot, "transfer").IssueActivity);
    }

    [Fact]
    public async Task AnUntypedActiveVoteLeavesVoteFiguresUnavailableAndRollbackKeepsReleaseNamesAndBillCountsTogether()
    {
        await using var host = await StartAsync();
        var database = host.OperationalDatabase;
        await OfficialsSeed.AddOfficialsAsync(database, new SeededOfficial("member", "Earlier Name", House("2025-01-03", "AL", 1)));
        await ViewsSeed.ExecuteAsync(database, """
            INSERT INTO release_issues (release_number, issue_id, name) VALUES (1, 'zz-target', 'Old Issue');
            """);
        await ViewsSeed.AddVotesAsync(database, new SeededVote('a', "2025-03-01") { PersonIds = ["member"], Casts = ["Yea"] });
        await StoredBillCounts.FillAsync(database, 1, 119, onlyStoredRevisions: true);
        await ViewsSeed.ExecuteAsync(database, $"""
            {ViewsSeed.ActivateReleaseSql(2, ReleaseDay.AddDays(1), 3)}
            INSERT INTO release_officials (release_number, official_id, name) VALUES (2, 'member', 'New Name');
            INSERT INTO release_official_terms (release_number, official_id, "position", office, kind, state, district, start_on)
            VALUES (2, 'member', 0, 'House', 'Representative', 'AL', 1, '2025-01-03');
            INSERT INTO release_issues (release_number, issue_id, name) VALUES (2, 'zz-target', 'New Issue');
            """);
        await BillsSeed.AddBillsAsync(database, new SeededBill('e', "2025-05-01")
        {
            FirstRelease = 2,
            Issues = ["zz-target"],
            Sponsorships = [new("member", "Sponsor", "2025-05-01")]
        });
        await StoredBillCounts.FillAsync(database, 2, 119, onlyStoredRevisions: true);
        await ViewsSeed.ExecuteAsync(database, $"""
            SET session_replication_role = replica;
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number)
            VALUES ('vote', '{new string('f', 64)}', 2, 1);
            """);

        var incomplete = await ReadAsync(host, IssuePath);
        Assert.Equal((2, "New Issue", "New Name"), (incomplete.Release, incomplete.Issue!.Name, Assert.Single(incomplete.Members).Name));
        Assert.Equal(new MapIssueActivityAnswer(new(1, 1), null), Assert.Single(incomplete.Members).IssueActivity);
        Assert.Null(Assert.Single((await ReadAsync(host, "/v1/maps")).Members).Attendance);

        var inFlight = await ReadWhileRollingBackAsync(host);
        Assert.Equal((2, "New Issue", "New Name"), (inFlight.Release, inFlight.Issue!.Name, Assert.Single(inFlight.Members).Name));
        Assert.Equal(new MapIssueActivityAnswer(new(1, 1), null), Assert.Single(inFlight.Members).IssueActivity);

        var restored = await ReadAsync(host, IssuePath);
        Assert.Equal((1, "Old Issue", "Earlier Name"), (restored.Release, restored.Issue!.Name, Assert.Single(restored.Members).Name));
        Assert.Equal(new MapIssueActivityAnswer(new(0, 0), new(0, 1)), Assert.Single(restored.Members).IssueActivity);
    }

    [Fact]
    public async Task InvalidFiltersAreRefusedAndAnEmptyReleaseKeepsItsIdentityWithoutInventingCalendarScope()
    {
        await using var host = await ApiHost.StartAsync(postgres);
        using var noRelease = await host.Client.GetAsync("/v1/maps");
        Assert.Equal(HttpStatusCode.ServiceUnavailable, noRelease.StatusCode);

        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, ReleaseDay, recordCount: 0);
        var empty = await ReadAsync(host, "/v1/maps");
        Assert.Equal(119, empty.Congress);
        Assert.Empty(empty.Members);
        Assert.Empty(empty.Seats);
        var earliest = await ReadAsync(host, "/v1/maps?congress=74");
        Assert.Equal(new DateOnly(1937, 1, 2), earliest.AsOf);
        Assert.Empty(earliest.Members);
        string[] invalidQueries =
        [
            "congress=120", "congress=1", $"congress={CongressCalendar.CongressOn(DateOnly.MaxValue)}", "congress=2147483647", "congress=119&congress=118",
            "chamber=1", "chamber=house&chamber=house", "layer=bad", "layer=attendance&layer=termEnds",
            "layer=issueActivity", "layer=issueActivity&issue=missing", "issue=anything"
        ];
        foreach (var query in invalidQueries)
        {
            using var response = await host.Client.GetAsync("/v1/maps?" + query);
            Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        }

        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 2, DateTimeOffset.UnixEpoch.AddYears(-45), recordCount: 0);
        var unsupported = await ReadAsync(host, "/v1/maps");
        Assert.Equal(2, unsupported.Release);
        Assert.Null(unsupported.Congress);
        Assert.Null(unsupported.AsOf);
        Assert.Empty(unsupported.Members);

        await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 3, DateTimeOffset.MaxValue, recordCount: 0);
        var lastDay = await ReadAsync(host, "/v1/maps");
        Assert.Equal(CongressCalendar.CongressOn(DateOnly.MaxValue), lastDay.Congress);
        Assert.Equal(DateOnly.MaxValue, lastDay.AsOf);
        Assert.Empty(lastDay.Members);
    }

    private async Task<ApiHost> StartAsync()
    {
        var host = await ApiHost.StartAsync(postgres);
        try
        {
            await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, ReleaseDay);
            return host;
        }
        catch
        {
            await host.DisposeAsync();
            throw;
        }
    }

    private static async Task<MapAnswer> ReadWhileRollingBackAsync(ApiHost host)
    {
        var timeout = TimeSpan.FromSeconds(15);
        using var gate = new FirstPublicApiReadGate(timeout);
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(gate).SetMinimumLevel(LogLevel.Trace));
        var dataSourceBuilder = new NpgsqlDataSourceBuilder(host.Options.PublicViewsReaderDatabase);
        dataSourceBuilder.UseLoggerFactory(loggerFactory);
        await using var views = dataSourceBuilder.Build();
        var store = new MapStore(new RecordSnapshot(views));
        var request = new MapRequest(null, PublishedChamber.House, MapLayer.IssueActivity, "zz-target");
        var read = Task.Run(() => store.FindAsync(request, default));
        try
        {
            await gate.FirstRead.WaitAsync(timeout);
            await ViewsSeed.ActivateReleaseAsync(host.OperationalDatabase, 1, ReleaseDay);
        }
        finally
        {
            gate.Resume();
            await read.WaitAsync(timeout);
        }
        return (await read)!;
    }

    private static async Task<MapAnswer> ReadAsync(ApiHost host, string path) =>
        (await host.Client.GetFromJsonAsync<MapAnswer>(path, PublicationProtocol.JsonOptions))!;

    private static MapMemberAnswer Member(MapAnswer answer, string id) => Assert.Single(answer.Members, member => member.OfficialId == id);

    private static (int Held, int Missed) Attendance(MapMemberAnswer member)
    {
        Assert.NotNull(member.Attendance);
        return (member.Attendance.Held, member.Attendance.Missed);
    }

    private static SeededTerm House(string startsOn, string state, int district) =>
        new("House", "Representative", startsOn) { State = state, District = district, Party = "Democrat" };

    private static SeededTerm Senate(string state) =>
        new("Senate", "Senator", "2025-01-03") { State = state, Party = "Democrat", EndsOn = "2031-01-03" };
}
