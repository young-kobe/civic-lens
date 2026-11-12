using CivicLens.PublicApi.Votes;
using CivicLens.PublicApi.ZipDistricts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi;
using CivicLens.Tests.PublicApi.Votes;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Publication.Contracts;
using Microsoft.Extensions.Logging;
using Npgsql;

namespace CivicLens.Tests.PublicApi.ZipDistricts;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ZipMemberSnapshotTests(PostgresCollection postgres)
{
    private const string PublicRole = "civic_lens_public";
    private const string PublicPassword = "test-only-password";

    [Fact]
    public async Task MemberDataAndSharedVotesStayOnTheReleaseReadByTheFirstDistrictQuery()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        using var gate = new FirstPublicApiReadGate(TimeSpan.FromSeconds(15));
        using var loggerFactory = LoggerFactory.Create(builder => builder.AddProvider(gate).SetMinimumLevel(LogLevel.Trace));
        Task<ZipLookup>? read = null;
        try
        {
            await ViewsSeed.ActivateReleaseAsync(database, 1, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
            await ZipSeed.AddRecordAsync(database, 1);
            await ZipSeed.AdmitDistrictsAsync(database);
            await ZipSeed.AddNamesAsync(database, 1,
                new SeededSeat("p0", "Pat Release One", "Representative", "AL", 2),
                new SeededSeat("p1", "Sam Release One", "Senator", "AL", null));
            await ViewsSeed.ExecuteAsync(database, $"""
                INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
                VALUES (2, '000002-{new string('0', 31)}1', 'test', {new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero).UtcTicks}, 1, false);
                """);
            await ViewsSeed.AddVotesAsync(database,
                new SeededVote('a', "2026-05-01") { FirstRelease = 1, LastRelease = 1, RecordId = 1.ToString("x64"), Casts = ["Yea"], PersonIds = ["p0"], Issues = ["health"] },
                new SeededVote('b', "2026-05-02") { Chamber = "Senate", FirstRelease = 1, LastRelease = 1, RecordId = 2.ToString("x64"), Casts = ["Nay"], PersonIds = ["p1"], Issues = ["health"] },
                new SeededVote('c', "2026-05-03") { FirstRelease = 2, RecordId = 3.ToString("x64"), Casts = ["Nay"], PersonIds = ["p0"], Issues = ["other"] },
                new SeededVote('d', "2026-05-04") { Chamber = "Senate", FirstRelease = 2, RecordId = 4.ToString("x64"), Casts = ["Yea"], PersonIds = ["p1"] });
            await BillsSeed.AddBillsAsync(database,
                new SeededBill('e', "2026-05-01") { BillNumber = 7, FirstRelease = 1, LastRelease = 1, Issues = ["health"], Sponsorships = [new("p0", "Sponsor", "2026-05-01")] },
                new SeededBill('f', "2026-05-02") { BillNumber = 8, FirstRelease = 2, Issues = ["health"], Sponsorships = [new("p0", "Sponsor", "2026-05-02")] },
                new SeededBill('9', "2026-05-03") { BillNumber = 9, FirstRelease = 2, Issues = ["health"], Sponsorships = [new("p0", "Sponsor", "2026-05-03")] });
            await ViewsSeed.ExecuteAsync(database, "INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (1, 'health', 'Health Release One', 'Health'), (1, 'zero', 'Zero Votes', 'No votes'), (2, 'health', 'Health Release Two', 'Health'), (2, 'zero', 'New Zero Votes', 'No votes');");
            await ZipSeed.AddRecordAsync(database, 2);
            await ZipSeed.AddNamesAsync(database, 2,
                new SeededSeat("p0", "Pat Release Two", "Representative", "AL", 2),
                new SeededSeat("p1", "Sam Release Two", "Senator", "AL", null));
            await StoredBillCounts.FillAsync(database, 1, 119, onlyStoredRevisions: true);
            await StoredBillCounts.FillAsync(database, 2, 119, onlyStoredRevisions: true);
            await using (var owner = new NpgsqlConnection(database))
            {
                await owner.OpenAsync();
                await using var command = new NpgsqlCommand($"ALTER ROLE {PublicRole} LOGIN PASSWORD '{PublicPassword}'", owner);
                await command.ExecuteNonQueryAsync();
            }

            var publicConnection = new NpgsqlConnectionStringBuilder(database)
            {
                Username = PublicRole,
                Password = PublicPassword
            }.ConnectionString;
            var dataSourceBuilder = new NpgsqlDataSourceBuilder(publicConnection);
            dataSourceBuilder.UseLoggerFactory(loggerFactory);
            await using (var views = dataSourceBuilder.Build())
            {
                var store = new ZipDistrictStore(views);
                var request = new ZipDistrictRequest { Zcta = "36009" };
                read = Task.Run(() => store.FindMembersAsync(request, "health", null, default));

                await gate.FirstRead.WaitAsync(TimeSpan.FromSeconds(15));
                await ViewsSeed.ActivateReleaseAsync(database, 2, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
                gate.Resume();
                var first = await read.WaitAsync(TimeSpan.FromSeconds(15));

                Assert.NotNull(first.Answer);
                Assert.Equal(1, first.Answer.Release);
                Assert.Equal([new ZipMemberIssueOption("health", "Health Release One"), new ZipMemberIssueOption("zero", "Zero Votes")], first.IssueOptions);
                Assert.Equal("Pat Release One", first.Answer.Districts[0].Representative!.Name);
                Assert.Equal("Sam Release One", first.Answer.States[0].Senators[0]!.Name);
                var firstHouseAttendance = Assert.Single(first.Answer.Districts[0].Representative!.MissedVotes);
                var firstSenateAttendance = Assert.Single(first.Answer.States[0].Senators[0]!.MissedVotes);
                Assert.Equal((PublishedChamber.House, 119, 0, 1), (firstHouseAttendance.Chamber, firstHouseAttendance.Congress, firstHouseAttendance.Missed, firstHouseAttendance.Held));
                Assert.Equal((PublishedChamber.Senate, 119, 0, 1), (firstSenateAttendance.Chamber, firstSenateAttendance.Congress, firstSenateAttendance.Missed, firstSenateAttendance.Held));
                var firstBills = first.Answer.Districts[0].Representative!.Bills!;
                Assert.Equal((119, 1, 1), (firstBills.Congress, firstBills.Sponsored, firstBills.BillsInCongress));
                var firstIssue = Assert.Single(first.Answer.Districts[0].Representative!.Issues!.Issues);
                Assert.Equal(("health", "Health Release One", 1), (firstIssue.IssueId, firstIssue.Name, firstIssue.SponsoredBills));
                Assert.NotNull(first.SharedVotes);
                Assert.Equal(1, first.SharedVotes.Release);
                Assert.Equal([2.ToString("x64"), 1.ToString("x64")], first.SharedVotes.Votes.Select(vote => vote.RecordId));
                Assert.Equal(["p0", "p1"], first.SharedVotes.OfficialIds);
                var firstHouse = Assert.Single(first.SharedVotes.Votes, vote => vote.Chamber == PublishedChamber.House);
                var firstSenate = Assert.Single(first.SharedVotes.Votes, vote => vote.Chamber == PublishedChamber.Senate);
                Assert.Equal((1.ToString("x64"), "Yea", "p0"), (firstHouse.RecordId, firstHouse.Casts[0].Cast, firstHouse.Casts[0].OfficialId));
                Assert.Equal((2.ToString("x64"), "Nay", "p1"), (firstSenate.RecordId, firstSenate.Casts[0].Cast, firstSenate.Casts[0].OfficialId));

                var second = await store.FindMembersAsync(request, "health", null, default);
                Assert.NotNull(second.Answer);
                Assert.Equal([new ZipMemberIssueOption("health", "Health Release Two"), new ZipMemberIssueOption("zero", "New Zero Votes")], second.IssueOptions);
                Assert.Equal(2, second.Answer.Release);
                Assert.Equal("Pat Release Two", second.Answer.Districts[0].Representative!.Name);
                Assert.Equal("Sam Release Two", second.Answer.States[0].Senators[0]!.Name);
                var secondBills = second.Answer.Districts[0].Representative!.Bills!;
                Assert.Equal((119, 2, 2), (secondBills.Congress, secondBills.Sponsored, secondBills.BillsInCongress));
                var secondIssues = second.Answer.Districts[0].Representative!.Issues!.Issues;
                var secondIssue = Assert.Single(secondIssues, figure => figure.IssueId == "health");
                Assert.Equal(("health", "Health Release Two", 2), (secondIssue.IssueId, secondIssue.Name, secondIssue.SponsoredBills));
                Assert.Equal(1, Assert.Single(secondIssues, figure => figure.IssueId == "other").VotesCast);
                Assert.NotNull(second.SharedVotes);
                Assert.Equal(2, second.SharedVotes.Release);
                Assert.Empty(second.SharedVotes.Votes);
                Assert.DoesNotContain(second.SharedVotes.Votes, vote => vote.RecordId == 1.ToString("x64") || vote.RecordId == 2.ToString("x64"));
            }
        }
        finally
        {
            gate.Resume();
            if (read is not null)
            {
                try { await read.WaitAsync(TimeSpan.FromSeconds(15)); }
                catch { }
            }
            try { await ViewsSeed.ExecuteAsync(database, $"ALTER ROLE {PublicRole} NOLOGIN PASSWORD NULL"); }
            finally { await postgres.DropDatabaseAsync(database); }
        }
    }
}
