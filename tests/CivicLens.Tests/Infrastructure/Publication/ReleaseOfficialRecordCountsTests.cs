using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Home;
using CivicLens.Tests.PublicApi.Officials;
using CivicLens.Tests.PublicApi.Statements;
using CivicLens.Tests.PublicApi.Votes;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Publication;

/// <summary>
/// The stored official record counts of each release against the live count of each public view, which the officials list and the record page counted before the publish stored them.
/// The seed covers each filter of the views: record ranges of two releases, a vote kind that is not substantive, a cosponsor, a statement without a summary, and an executive action without a title.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ReleaseOfficialRecordCountsTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly DateTimeOffset Published = new(2026, 6, 2, 0, 0, 0, TimeSpan.Zero);
    private string database = null!;

    // The counts of the views as the readers read them before this change: the record count of public_api.officials, and the four tab counts of the record page.
    private const string LiveSql = """
        SELECT o.official_id
            || '|' || (SELECT count(*) FROM public_api.active_records a
                         JOIN published_entry_officials e ON e.kind = a.kind AND e.record_id = a.record_id AND e.revision_number = a.revision_number
                        WHERE e.official_id = o.official_id)
            || '|' || (SELECT count(*) FROM public_api.member_votes v WHERE v.official_id = o.official_id AND v.kind = ANY(@kinds))
            || '|' || (SELECT count(*) FROM public_api.bill_sponsorships p
                        WHERE p.official_id = o.official_id AND p.role = 'Sponsor'
                          AND EXISTS (SELECT 1 FROM public_api.bills b WHERE b.record_id = p.record_id AND b.revision_number = p.revision_number))
            || '|' || (SELECT count(*) FROM public_api.statements s
                        WHERE EXISTS (SELECT 1 FROM public_api.statement_speakers p
                                       WHERE p.record_id = s.record_id AND p.revision_number = s.revision_number AND p.official_id = o.official_id))
            || '|' || (SELECT count(*) FROM public_api.executive_actions e WHERE e.official_id = o.official_id)
          FROM public_api.officials o ORDER BY o.official_id
        """;

    private const string StoredSql = """
        SELECT o.official_id || '|' || o.record_count || '|' || c.votes || '|' || c.bills || '|' || c.statements || '|' || c.executive_actions
          FROM public_api.officials o
          JOIN public_api.release_official_record_counts c ON c.official_id = o.official_id
         ORDER BY o.official_id
        """;

    public async Task InitializeAsync() => database = await postgres.CreateMigratedDatabaseAsync();

    public Task DisposeAsync() => postgres.DropDatabaseAsync(database);

    [Fact]
    public async Task TheStoredCountsOfEachReleaseEqualTheLiveCountOfTheListAndOfEachTab()
    {
        await SeedAsync();
        await StoredBillCounts.RefreshRecordCountsAsync(database, 1);
        await StoredBillCounts.RefreshRecordCountsAsync(database, 2);

        var seen = new List<string>();
        foreach (var release in new[] { 1, 2 })
        {
            await ViewsSeed.ActivateReleaseAsync(database, release, Published.AddDays(release));
            var stored = await ReadAsync(StoredSql);
            Assert.Equal(await ReadAsync(LiveSql), stored);
            Assert.Equal(4, stored.Length);
            seen.AddRange(stored);
        }
        // Every count is nonzero in some release and differs between the releases, so the comparison covers each column and the record ranges.
        var columns = seen.Select(row => row.Split('|')).ToArray();
        Assert.All(Enumerable.Range(1, 5), column => Assert.Contains(columns, row => row[column] != "0"));
        Assert.NotEqual(seen.Take(4), seen.Skip(4));
    }

    private async Task SeedAsync()
    {
        await ViewsSeed.ActivateReleaseAsync(database, 1, Published.AddDays(1));
        await ViewsSeed.ActivateReleaseAsync(database, 2, Published.AddDays(2));
        await OfficialsSeed.AddOfficialsAsync(database, new SeededOfficial("p1", "Ann Able"), new SeededOfficial("p2", "Ben Baker"),
            new SeededOfficial("p3", "Cy Cole"), new SeededOfficial("president", "Eve East"));
        await ViewsSeed.ExecuteAsync(database, "INSERT INTO release_officials (release_number, official_id, name) SELECT 2, official_id, name FROM release_officials WHERE release_number = 1");
        await ViewsSeed.AddVotesAsync(database,
            new SeededVote('a', "2026-02-01") { Casts = ["Yea", "Nay"], PersonIds = ["p1", "p2"], RollCall = 1 },
            new SeededVote('b', "2026-02-02") { Kind = "Cloture", Casts = ["Yea"], PersonIds = ["p1"], RollCall = 2 },
            new SeededVote('c', "2026-02-03") { Kind = "Suspension", Casts = ["Yea", "Nay"], PersonIds = ["p1", "p3"], RollCall = 3, LastRelease = 1 },
            new SeededVote('d', "2026-02-04") { Casts = ["Nay"], PersonIds = ["p2"], RollCall = 4, FirstRelease = 2 });
        await BillsSeed.AddBillsAsync(database,
            new SeededBill('1', "2026-03-01") { BillNumber = 1, Sponsorships = [new("p1", "Sponsor", "2026-01-10"), new("p2", "Cosponsor", "2026-01-10")] },
            new SeededBill('2', "2026-03-02") { BillNumber = 2, Sponsorships = [new("p2", "Sponsor", "2026-01-11")], LastRelease = 1 },
            new SeededBill('3', "2026-03-03") { BillNumber = 3, Sponsorships = [new("p1", "Sponsor", "2026-01-12")], FirstRelease = 2 });
        await StatementsSeed.AddAsync(database,
            new SeededStatement('e', "2026-04-01", ["p1", "p3"], [], "Person", "Person"),
            new SeededStatement('f', "2026-04-02", ["p1"], [], "Person", "Person") { LastRelease = 1 },
            new SeededStatement('g', "2026-04-03", ["p2"], [], "Person", "Person"));
        await HomeSeed.AddExecutiveActionsAsync(database,
            new SeededExecutiveAction('8', "ExecutiveOrder", "2026-05-20", "2026-05-21"),
            new SeededExecutiveAction('9', "Proclamation", "2026-05-22", "2026-05-23"));
        await OfficialsSeed.AddEntriesAsync(database, new SeededEntry("statement", 'e', "p1", "p3"), new SeededEntry("statement", 'f', "p1") { LastRelease = 1 },
            new SeededEntry("executiveAction", '8', "president") { DocumentType = "Executive Order" });
        // A statement without a summary and an executive action without a title are not public, so no count includes them.
        await ViewsSeed.ExecuteAsync(database, $"""
            UPDATE statement_revisions SET summary = NULL WHERE candidate_id = '{new string('g', 64)}';
            UPDATE executive_action_revisions SET title = NULL WHERE record_id = '{new string('9', 64)}';
            """);
    }

    private async Task<string[]> ReadAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        if (sql.Contains("@kinds", StringComparison.Ordinal)) command.Parameters.AddWithValue("kinds", PublishedVoteKindExtensions.SubstantiveNames());
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(reader.GetString(0));
        return [.. rows];
    }
}
