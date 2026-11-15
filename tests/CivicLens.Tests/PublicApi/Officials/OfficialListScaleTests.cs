using System.Diagnostics;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Officials;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Npgsql;
using Xunit.Abstractions;

namespace CivicLens.Tests.PublicApi.Officials;

/// <summary>
/// The list statements of <see cref="OfficialStore"/> at the production size of 542 officials, read as <c>civic_lens_public</c>.
/// Each official has two terms, and 2,000 statements name them. A walk of 50 rows a page visits every official once in sort order.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class OfficialListScaleTests(PostgresCollection postgres, ITestOutputHelper output)
{
    private const int Officials = 542;
    private const int Statements = 2_000;
    private static readonly TimeSpan Budget = TimeSpan.FromMilliseconds(250);

    [Fact]
    public async Task EveryListStatementOfFiveHundredFortyTwoOfficialsRunsInsideTheBudgetAndAWalkKeepsTheSortOrder()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await SeedAsync(database);
            await using var views = NpgsqlDataSource.Create(await ApiHost.LogInPublicRoleAsync(database));
            var store = new OfficialStore(views);
            var first = await store.ListAsync(new OfficialRequest(), default);

            var walk = new List<string>();
            OfficialCursor? cursor = null;
            var slowest = TimeSpan.Zero;
            do
            {
                var (page, elapsed) = await TimeAsync(store, new OfficialRequest { Cursor = cursor });
                slowest = elapsed > slowest ? elapsed : slowest;
                walk.AddRange(page.Officials.Select(official => official.OfficialId));
                cursor = OfficialCursor.Parse(page.NextCursor);
            }
            while (cursor is not null);
            var (_, filtered) = await TimeAsync(store, new OfficialRequest { Kind = PublishedOfficeKind.Senator, Party = "democrat", NamePrefix = "van" });
            var (_, exact) = await TimeAsync(store, new OfficialRequest { NamePrefix = "member 0007 van hollen", ExactNameFirst = true });

            output.WriteLine($"Slowest walk page {slowest.TotalMilliseconds:F1} ms. Filtered {filtered.TotalMilliseconds:F1} ms. Exact name first {exact.TotalMilliseconds:F1} ms. Budget {Budget.TotalMilliseconds:F0} ms.");
            Assert.Equal(Officials, first!.OfficialsInRelease);
            Assert.Equal(await ExpectedOrderAsync(database), walk);
            Assert.All(new[] { slowest, filtered, exact }, elapsed => Assert.True(elapsed < Budget, $"A list statement took {elapsed}."));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    private static async Task<(OfficialPage Page, TimeSpan Elapsed)> TimeAsync(OfficialStore store, OfficialRequest request)
    {
        var clock = Stopwatch.StartNew();
        var page = await store.ListAsync(request, default);
        return (page!, clock.Elapsed);
    }

    // The order of the rule: the roster last name, else the name, then the name, then the ID.
    private static async Task<string[]> ExpectedOrderAsync(string database)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT official_id FROM release_officials WHERE release_number = 1 ORDER BY coalesce(sort_name, name), name, official_id", connection);
        var ids = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) ids.Add(reader.GetString(0));
        return [.. ids];
    }

    // Every seventh official has the compound surname Van Hollen, so many rows share a sort name. Every hundredth has no sort name.
    private static Task SeedAsync(string database) => ViewsSeed.ExecuteAsync(database, $"""
        {ViewsSeed.ActivateReleaseSql(1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero), Statements)}
        INSERT INTO release_officials (release_number, official_id, name, sort_name)
        SELECT 1, 'p' || lpad(i::text, 4, '0'),
               CASE WHEN i % 7 = 0 THEN 'Member ' || lpad((i % 13)::text, 4, '0') || ' Van Hollen' ELSE 'Member ' || lpad(i::text, 4, '0') || ' Last' || (i % 50) END,
               CASE WHEN i % 100 = 0 THEN NULL WHEN i % 7 = 0 THEN 'Van Hollen' ELSE 'Last' || (i % 50) END
          FROM generate_series(1, {Officials}) i;
        INSERT INTO release_official_terms (release_number, official_id, "position", office, kind, state, district, party, start_on, end_on)
        SELECT 1, 'p' || lpad(i::text, 4, '0'), t, CASE WHEN i % 5 = 0 THEN 'Senator' ELSE 'Representative' END,
               CASE WHEN i % 5 = 0 THEN 'Senator' ELSE 'Representative' END, 'S' || chr(65 + i % 26), CASE WHEN i % 5 = 0 THEN NULL ELSE 1 END,
               CASE WHEN (i + t) % 2 = 0 THEN 'Democrat' ELSE 'Republican' END, DATE '2021-01-03' + t * 730, DATE '2023-01-03' + t * 730
          FROM generate_series(1, {Officials}) i CROSS JOIN generate_series(0, 1) t;
        SET session_replication_role = replica;
        INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
        SELECT 'statement', lpad(to_hex(s), 64, '0'), 1, 1, NULL FROM generate_series(1, {Statements}) s;
        INSERT INTO published_entries (kind, record_id, revision_number, headline, statement_candidate_id, statement_revision_number)
        SELECT 'statement', lpad(to_hex(s), 64, '0'), 1, 'H', lpad(to_hex(s), 64, '0'), 1 FROM generate_series(1, {Statements}) s;
        INSERT INTO published_entry_officials (kind, record_id, revision_number, "position", official_id)
        SELECT 'statement', lpad(to_hex(s), 64, '0'), 1, 0, 'p' || lpad((1 + s % {Officials})::text, 4, '0') FROM generate_series(1, {Statements}) s;
        SET session_replication_role = origin;
        ANALYZE;
        """);
}
