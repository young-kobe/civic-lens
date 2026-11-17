using System.Diagnostics;
using System.Globalization;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Officials;
using CivicLens.PublicWeb.Publication;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Npgsql;
using Xunit.Abstractions;

namespace CivicLens.Tests.PublicApi.Officials;

/// <summary>
/// The votes tab of the record page of one official at the production size of 1,600 votes, read as <c>civic_lens_public</c>, which runs with jit off.
/// Each House vote names 440 members and each Senate vote names 100. Most votes have a substantive kind, and the official voted on every House vote.
/// The budget holds for the whole page read, so it also holds for each statement of <see cref="OfficialRecordPageReader"/>.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class OfficialRecordVotesScaleTests(PostgresCollection postgres, ITestOutputHelper output)
{
    private const int HouseVotes = 1_000;
    private const int SenateVotes = 600;
    private const int HouseMembers = 440;
    private const int SenateMembers = 100;
    private const string Official = "h0001";
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(250);

    // The page size of the votes tab. OfficialRecordQueries is internal to PublicApi, so the test repeats the value.
    private const int PageSize = 25;

    // Each vote filter of the tab, alone, so each statement shape runs once.
    private static readonly (string Name, OfficialRecordRequest Request)[] Filters =
    [
        ("issue", new(OfficialRecordKind.Votes, null) { Issue = "health" }),
        ("bill", new(OfficialRecordKind.Votes, null) { BillType = "hr", BillNumber = 17 }),
        ("party", new(OfficialRecordKind.Votes, null) { AgainstPartyOnly = true }),
        ("cast", new(OfficialRecordKind.Votes, null) { Cast = OfficialRecordRequest.No })
    ];

    [Fact]
    public async Task TheFirstPageAFilteredPageAndTheNextPageOfTheVotesTabOfAHouseMemberWithOneThousandVotesReadInsideTheBudget()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await SeedAsync(database);
            await StoredBillCounts.RefreshRecordCountsAsync(database, 1);
            await using var views = NpgsqlDataSource.Create(await ApiHost.LogInPublicRoleAsync(database));
            var reader = new OfficialRecordPageReader(views);
            // One untimed read opens the connection, as the first list read of OfficialListScaleTests does.
            await reader.ReadAsync(Official, new(OfficialRecordKind.Votes, null), default);

            var (first, firstElapsed) = await TimeAsync(reader, new(OfficialRecordKind.Votes, null));
            var (next, nextElapsed) = await TimeAsync(reader, new(OfficialRecordKind.Votes, OfficialRecordCursor.Parse(first.NextCursor)));
            var filtered = new List<(string Name, int Rows, TimeSpan Elapsed)>();
            foreach (var (name, request) in Filters)
            {
                var (page, elapsed) = await TimeAsync(reader, request);
                filtered.Add((name, page.Votes.Length, elapsed));
            }

            output.WriteLine($"First page {firstElapsed.TotalMilliseconds:F1} ms. Next page {nextElapsed.TotalMilliseconds:F1} ms. " +
                string.Join(" ", filtered.Select(page => $"Filter {page.Name} {page.Elapsed.TotalMilliseconds:F1} ms ({page.Rows} rows).")) +
                $" Budget {Budget.TotalMilliseconds:F0} ms.");
            Assert.Equal(await SubstantiveVoteCountAsync(database), first.Counts.Single(tab => tab.Kind == OfficialRecordKind.Votes).Count);
            Assert.Equal(PageSize, first.Votes.Length);
            Assert.Equal(PageSize, next.Votes.Length);
            Assert.Empty(first.Votes.Select(vote => vote.RecordId).Intersect(next.Votes.Select(vote => vote.RecordId)));
            Assert.All(filtered, page => Assert.True(page.Rows > 0, $"The {page.Name} filter found no vote."));
            Assert.All(new[] { firstElapsed, nextElapsed }.Concat(filtered.Select(page => page.Elapsed)),
                elapsed => Assert.True(elapsed < Budget, $"A votes tab read took {elapsed}."));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    private static async Task<(OfficialRecordPageData Page, TimeSpan Elapsed)> TimeAsync(OfficialRecordPageReader reader, OfficialRecordRequest request)
    {
        var clock = Stopwatch.StartNew();
        var page = await reader.ReadAsync(Official, request, default);
        return (page!, clock.Elapsed);
    }

    private static async Task<int> SubstantiveVoteCountAsync(string database) => int.Parse((await ViewsSeed.QueryAsync(database, $"""
        SELECT count(*)::text FROM public_api.member_votes WHERE official_id = '{Official}' AND kind = ANY(ARRAY['{string.Join("', '",
            PublishedVoteKindExtensions.SubstantiveNames())}'])
        """)).Single(), CultureInfo.InvariantCulture);

    // One template vote of each chamber holds the full evidence rows. Each copy repeats the revision of its template under the record ID md5(chamber || n),
    // with its own member list, roll call, day, kind, bill, issue, and party split, as the copies of TypedReleaseReadScaleTests do.
    private static async Task SeedAsync(string database)
    {
        await ViewsSeed.ActivateReleaseAsync(database, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero), HouseVotes + SenateVotes);
        await ViewsSeed.AddVotesAsync(database,
            new SeededVote('a', "2025-01-03") { Casts = [.. Enumerable.Repeat("Yea", HouseMembers)], PersonIds = [.. Members("h", HouseMembers)] },
            new SeededVote('b', "2025-01-03") { Chamber = "Senate", Casts = [.. Enumerable.Repeat("Yea", SenateMembers)], PersonIds = [.. Members("s", SenateMembers)] });
        await ViewsSeed.ExecuteAsync(database, $"""
            SET session_replication_role = replica;
            {ProductionSizeVotes.CopiesSql('a', "House", HouseVotes, HouseMembers)}
            {ProductionSizeVotes.CopiesSql('b', "Senate", SenateVotes, SenateMembers)}
            INSERT INTO release_officials (release_number, official_id, name)
            SELECT 1, 'h' || lpad(i::text, 4, '0'), 'Representative ' || i FROM generate_series(1, {HouseMembers}) i
             UNION ALL SELECT 1, 's' || lpad(i::text, 4, '0'), 'Senator ' || i FROM generate_series(1, {SenateMembers}) i;
            INSERT INTO release_official_terms (release_number, official_id, "position", office, kind, state, district, party, start_on, end_on)
            VALUES (1, '{Official}', 0, 'Representative', 'Representative', 'MA', 7, 'Democrat', DATE '2025-01-03', NULL);
            INSERT INTO release_issues (release_number, issue_id, name, definition) VALUES (1, 'health', 'Health care', 'Defined');
            SET session_replication_role = origin;
            ANALYZE;
            """);
    }

    private static IEnumerable<string> Members(string prefix, int count) => Enumerable.Range(1, count).Select(i => prefix + i.ToString("0000", CultureInfo.InvariantCulture));
}
