using System.Diagnostics;
using System.Globalization;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Officials;
using CivicLens.PublicWeb.Publication;
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
            {CopiesSql('a', "House", HouseVotes, HouseMembers)}
            {CopiesSql('b', "Senate", SenateVotes, SenateMembers)}
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

    // Votes spread over 700 days, so several votes share a day. Three of every four have a substantive kind. A third split the parties.
    // Members of the first half are Democrats. A member votes No on one vote in three and does not vote on one in fifty.
    private static string CopiesSql(char template, string chamber, int votes, int members) => $$"""
        CREATE TEMP TABLE copies_{{template}} AS
        SELECT n, CASE WHEN n = 0 THEN repeat('{{template}}', 64) ELSE md5('{{chamber}}' || n) || md5('x' || n) END AS id,
               (ARRAY['Passage', 'Suspension', 'Amendment', 'Nomination', 'Rule', 'Passage', 'ConferenceReport', 'Amendment', 'Cloture', 'VetoOverride', 'Passage', 'MotionToRecommit'])[1 + n % 12] AS kind
          FROM generate_series(0, {{votes - 1}}) n;
        INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
        SELECT 'vote', id, 1, 1, NULL FROM copies_{{template}} WHERE n > 0;
        INSERT INTO vote_member_lists
        SELECT (jsonb_populate_record(NULL::vote_member_lists, to_jsonb(l) || jsonb_build_object('list_id', c.id))).*
          FROM vote_member_lists l, copies_{{template}} c WHERE l.list_id = repeat('{{template}}', 64) AND c.n > 0;
        INSERT INTO vote_revisions
        SELECT (jsonb_populate_record(NULL::vote_revisions, to_jsonb(v) || jsonb_build_object('record_id', c.id, 'member_list_id', c.id, 'roll_call_number', c.n + 1)
                 || jsonb_build_object('candidate_id', 'cand' || c.n))).*
          FROM vote_revisions v, copies_{{template}} c WHERE v.record_id = repeat('{{template}}', 64) AND c.n > 0;
        UPDATE vote_revisions v SET voted_on = DATE '2025-01-03' + c.n * 700 / {{votes}}, kind = c.kind, bill_type = 'hr', bill_number = 1 + c.n % 400,
               columns_written = true, alignment = CASE WHEN c.n % 3 = 0 THEN 'Split' ELSE 'PartiesAgree' END,
               democrat_majority = 'Yea', republican_majority = CASE WHEN c.n % 3 = 0 THEN 'Nay' ELSE 'Yea' END
          FROM copies_{{template}} c WHERE v.record_id = c.id;
        INSERT INTO vote_member_list_entries (list_id, "position", person_id, bioguide_id, vote_cast, citation_extraction_id, citation_start, citation_length, party)
        SELECT c.id, e."position", e.person_id, e.bioguide_id,
               CASE WHEN (c.n + e."position") % 50 = 0 THEN 'Not Voting' WHEN (c.n + e."position") % 3 = 0 THEN 'Nay' ELSE 'Yea' END,
               e.citation_extraction_id, e.citation_start, e.citation_length, CASE WHEN e."position" < {{members / 2}} THEN 'Democrat' ELSE 'Republican' END
          FROM vote_member_list_entries e, copies_{{template}} c WHERE e.list_id = repeat('{{template}}', 64) AND c.n > 0;
        UPDATE vote_member_list_entries SET party = CASE WHEN "position" < {{members / 2}} THEN 'Democrat' ELSE 'Republican' END WHERE list_id = repeat('{{template}}', 64);
        INSERT INTO vote_revision_issues (record_id, revision_number, issue_id, position, is_tracked)
        SELECT id, 1, 'health', 0, true FROM copies_{{template}} WHERE n % 4 = 1;
        """;
}
