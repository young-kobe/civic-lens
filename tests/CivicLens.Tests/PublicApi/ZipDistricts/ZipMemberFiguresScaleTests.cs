using System.Diagnostics;
using System.Globalization;
using CivicLens.PublicApi.ZipDistricts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Votes;
using Npgsql;
using Xunit.Abstractions;

namespace CivicLens.Tests.PublicApi.ZipDistricts;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ZipMemberFiguresScaleTests(PostgresCollection postgres, ITestOutputHelper output)
{
    private const int HouseVotes = 1_000;
    private const int SenateVotes = 600;
    private const int HouseMembers = 440;
    private const int SenateMembers = 100;
    private const int Bills = 19_600;
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(500);

    [Fact]
    public async Task OneAndTwoDistrictMemberReadsKeepTheirFiguresInsideTheBudgetAtProductionSize()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await SeedAsync(database);
            await StoredBillCounts.FillAsync(database, 1, 119);
            await using var views = NpgsqlDataSource.Create(await ApiHost.LogInPublicRoleAsync(database));
            var store = new ZipDistrictStore(views);
            await store.FindMembersAsync(new() { Zcta = "36009" }, null, null, default);
            foreach (var (zcta, districts) in new[] { ("99999", 1), ("36009", 2) })
            {
                var clock = Stopwatch.StartNew();
                var lookup = await store.FindMembersAsync(new() { Zcta = zcta }, null, null, default);
                clock.Stop();
                output.WriteLine($"ZIP {zcta}: {clock.Elapsed.TotalMilliseconds:F1} ms. Budget {Budget.TotalMilliseconds:F0} ms.");
                var answer = Assert.IsType<ZipDistrictAnswer>(lookup.Answer);
                Assert.Equal(districts, answer.Districts.Length);
                Assert.Equal(1, answer.Release);
                var representative = Assert.Single(answer.Districts, district => district.District == 2).Representative!;
                var attendance = Assert.Single(representative.MissedVotes);
                Assert.Equal((119, HouseVotes, 19), (attendance.Congress, attendance.Held, attendance.Missed));
                Assert.Equal(Bills, representative.Bills!.BillsInCongress);
                Assert.Equal(45, representative.Bills.Sponsored);
                var issue = Assert.Single(representative.Issues!.Issues);
                Assert.Equal(("health", 45, 250), (issue.IssueId, issue.SponsoredBills, issue.VotesCast));
                var senators = Assert.Single(answer.States).Senators.OfType<SeatHolder>().ToArray();
                Assert.Equal(2, senators.Length);
                Assert.All(senators, senator => Assert.Equal(SenateVotes, Assert.Single(senator.MissedVotes).Held));
                Assert.Equal(150, Assert.Single(senators[0].Issues!.Issues).VotesCast);
                Assert.DoesNotContain(representative.Issues.Issues, figure => figure.IssueId == "untracked");
                Assert.Equal(20, lookup.SharedVotes!.Votes.Length);
                Assert.Equal(districts + 2, lookup.SharedVotes.OfficialIds.Length);
                Assert.True(clock.Elapsed < Budget, $"ZIP {zcta} took {clock.Elapsed}.");
            }
        }
        finally
        {
            try { await ApiHost.LogOutPublicRoleAsync(database); }
            finally { await postgres.DropDatabaseAsync(database); }
        }
    }

    private static async Task SeedAsync(string database)
    {
        await ViewsSeed.ActivateReleaseAsync(database, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero), Bills + HouseVotes + SenateVotes);
        await ZipSeed.AdmitDistrictsAsync(database, DistrictFixtures.Row("0102", "99999", 100));
        await ViewsSeed.AddVotesAsync(database,
            new SeededVote('a', "2025-01-03") { Casts = [.. Enumerable.Repeat("Yea", HouseMembers)], PersonIds = Members("h", HouseMembers) },
            new SeededVote('b', "2025-01-03") { Chamber = "Senate", Casts = [.. Enumerable.Repeat("Yea", SenateMembers)], PersonIds = Members("s", SenateMembers) });
        await BillsSeed.AddBillsAsync(database,
            new SeededBill('c', "2025-01-03") { BillNumber = 1, Issues = ["health"], Sponsorships = [new("h0001", "Sponsor", "2025-01-03")] });
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand($"""
            SET session_replication_role = replica;
            {ProductionSizeVotes.CopiesSql('a', "House", HouseVotes, HouseMembers)}
            {ProductionSizeVotes.CopiesSql('b', "Senate", SenateVotes, SenateMembers)}
            UPDATE vote_member_list_entries SET party = 'Republican' WHERE list_id = repeat('a', 64) AND person_id = 'h0001';
            INSERT INTO vote_revision_issues (record_id, revision_number, issue_id, position, is_tracked)
            SELECT id, 1, 'health', 0, true FROM copies_a WHERE kind IN ('Rule', 'Cloture', 'MotionToRecommit')
            UNION ALL SELECT id, 1, 'health', 0, true FROM copies_b WHERE kind IN ('Rule', 'Cloture', 'MotionToRecommit');
            {BillCopiesSql}
            INSERT INTO release_officials (release_number, official_id, name)
            SELECT 1, 'h' || lpad(i::text, 4, '0'), 'Representative ' || i FROM generate_series(1, {HouseMembers}) i
            UNION ALL SELECT 1, 's' || lpad(i::text, 4, '0'), 'Senator ' || i FROM generate_series(1, {SenateMembers}) i;
            INSERT INTO release_official_terms (release_number, official_id, position, office, kind, state, district, party, start_on)
            VALUES (1, 'h0001', 0, 'Representative', 'Representative', 'AL', 2, 'Democrat', '2025-01-03'),
                   (1, 'h0002', 0, 'Representative', 'Representative', 'AL', 1, 'Democrat', '2025-01-03'),
                   (1, 's0001', 0, 'Senator', 'Senator', 'AL', NULL, 'Democrat', '2025-01-03'),
                   (1, 's0002', 0, 'Senator', 'Senator', 'AL', NULL, 'Democrat', '2025-01-03');
            INSERT INTO release_issues (release_number, issue_id, name, definition)
            VALUES (1, 'health', 'Health', 'Health'), (1, 'budget', 'Budget', 'Budget');
            SET session_replication_role = origin;
            ANALYZE;
            """, connection) { CommandTimeout = 120 };
        await command.ExecuteNonQueryAsync();
    }

    private static string[] Members(string prefix, int count) =>
        [.. Enumerable.Range(1, count).Select(number => prefix + number.ToString("0000", CultureInfo.InvariantCulture))];

    private static string BillCopiesSql => $$"""
        CREATE TEMP TABLE bill_copies AS
        SELECT n, CASE WHEN n = 0 THEN repeat('c', 64) ELSE md5('bill' || n) || md5('bill-x' || n) END AS id
          FROM generate_series(0, {{Bills - 1}}) n;
        INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
        SELECT 'action', id, 1, 1, NULL FROM bill_copies WHERE n > 0;
        INSERT INTO action_revisions
        SELECT (jsonb_populate_record(NULL::action_revisions, to_jsonb(r) || jsonb_build_object(
            'record_id', c.id, 'run_id', 'bill-run-' || c.n, 'bill_number', c.n + 1))).*
          FROM action_revisions r, bill_copies c WHERE r.record_id = repeat('c', 64) AND c.n > 0;
        DELETE FROM action_revision_sponsorships WHERE record_id = repeat('c', 64);
        INSERT INTO action_revision_sponsorships (record_id, revision_number, position, person_id, bioguide_id, role, is_original, sponsored_on)
        SELECT c.id, 1, p, 'h' || lpad((1 + (c.n + p) % {{HouseMembers}})::text, 4, '0'),
               'B' || (1 + (c.n + p) % {{HouseMembers}}), CASE WHEN p = 0 THEN 'Sponsor' ELSE 'Cosponsor' END, true, DATE '2025-01-03'
          FROM bill_copies c CROSS JOIN generate_series(0, 12) p;
        INSERT INTO action_revision_issues (record_id, revision_number, issue_id, position, is_tracked)
        SELECT id, 1, CASE n % 4 WHEN 0 THEN 'health' WHEN 1 THEN 'budget' ELSE 'untracked' END, 0, n % 4 <> 2
          FROM bill_copies WHERE n > 0 AND n % 4 <> 3;
        """;
}
