using System.Diagnostics;
using System.Text.RegularExpressions;
using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using Xunit.Abstractions;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed partial class ReleaseVoteBillsMigrationTests(PostgresCollection postgres, ITestOutputHelper output)
{
    private const string Migration = "20261111194004_ReleaseVoteBills";
    private const int Bills = 5000;
    private const string VoteLinks = "SELECT record_id || '|' || coalesce(bill_record_id, 'none') FROM public_api.votes ORDER BY record_id";
    private const string ViewColumns = """
        SELECT a.attnum || '|' || a.attname || '|' || format_type(a.atttypid, a.atttypmod)
          FROM pg_attribute a WHERE a.attrelid = 'public_api.votes'::regclass AND a.attnum > 0 AND NOT a.attisdropped ORDER BY a.attnum
        """;

    // The order and the lateral shape of VoteStore.ListSql, with no filter, for the first page of three rows.
    private const string ListQuery = """
        SELECT a.release_number, v.record_id, v.bill_record_id, v.yea
          FROM (SELECT release_number FROM public_api.active_release LIMIT 1) a
          LEFT JOIN LATERAL (
               SELECT v.* FROM public_api.votes v
                WHERE TRUE
                ORDER BY v.voted_on DESC, v.chamber, v.session_number DESC, v.roll_call_number DESC, v.record_id LIMIT 3) v
            ON TRUE
         ORDER BY v.voted_on DESC, v.chamber, v.session_number DESC, v.roll_call_number DESC, v.record_id
        """;

    [Fact]
    public async Task UpgradeKeepsEveryColumnOfTheVoteViewShowsNoLinkUntilTheBackfillAndRollbackRestoresTheGroupedLinks()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var db = Context(database);
            var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, Migration) < 0);
            await db.GetService<IMigrator>().MigrateAsync(previous);
            await SeedAsync(database);
            var columns = await RowsAsync(database, ViewColumns);
            var grouped = await RowsAsync(database, VoteLinks);
            Assert.Equal([$"{new string('7', 64)}|{new string('b', 64)}", $"{new string('8', 64)}|none"], grouped);

            await PostgresCollectionAttemptStore.FromConnectionString(database).MigrateAsync();

            Assert.Equal(columns, await RowsAsync(database, ViewColumns));
            Assert.Equal([$"{new string('7', 64)}|none", $"{new string('8', 64)}|none"], await RowsAsync(database, VoteLinks));
            await PostgresStore(database).AddVoteBillsAsync(1, [new(new string('7', 64), new string('b', 64))], default);
            Assert.Equal(grouped, await RowsAsync(database, VoteLinks));
            await ExecuteAsync(database, "SET ROLE civic_lens_public; SELECT bill_record_id FROM public_api.votes LIMIT 0");

            await db.GetService<IMigrator>().MigrateAsync(previous);

            Assert.Equal(columns, await RowsAsync(database, ViewColumns));
            Assert.Equal(grouped, await RowsAsync(database, VoteLinks));
            Assert.Equal(["none"], await RowsAsync(database, "SELECT coalesce(to_regclass('public.release_vote_bills')::text, 'none')"));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task TheFirstPageOfTheVoteListReadsOnePinnedRowForEachVoteAndNoAggregateOfTheBillView()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var db = Context(database);
            var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, Migration) < 0);
            await db.GetService<IMigrator>().MigrateAsync(previous);
            await SeedManyBillsAsync(database);
            var oldPlan = await ExplainAsync(database);
            var grouped = await RowsAsync(database, VoteLinks);

            await PostgresCollectionAttemptStore.FromConnectionString(database).MigrateAsync();
            await ExecuteAsync(database, $"""
                INSERT INTO release_vote_bills (release_number, vote_record_id, bill_record_id)
                SELECT 1, r.record_id, md5(r.bill_number::text) || md5('x' || r.bill_number::text) FROM vote_revisions r;
                ANALYZE release_vote_bills;
                """);
            var newPlan = await ExplainAsync(database);

            var (oldBuffers, newBuffers) = (BuffersOf(oldPlan), BuffersOf(newPlan));
            output.WriteLine($"Old view, {Bills} bills: {oldBuffers} buffers.\n{oldPlan}\nNew view: {newBuffers} buffers.\n{newPlan}");
            Assert.Equal(grouped, await RowsAsync(database, VoteLinks));
            Assert.Contains("Aggregate", oldPlan, StringComparison.Ordinal);
            Assert.DoesNotContain("Aggregate", newPlan, StringComparison.Ordinal);
            Assert.DoesNotContain("Seq Scan on action_revisions", newPlan, StringComparison.Ordinal);
            Assert.Contains("release_vote_bills", newPlan, StringComparison.Ordinal);
            Assert.True(newBuffers * 10 < oldBuffers, $"The new plan read {newBuffers} buffers and the old plan read {oldBuffers}.");
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    private static int BuffersOf(string plan)
    {
        var line = BuffersLine().Match(plan);
        return int.Parse(line.Groups["hit"].Value) + (line.Groups["read"].Success ? int.Parse(line.Groups["read"].Value) : 0);
    }

    [GeneratedRegex(@"Buffers: shared hit=(?<hit>\d+)(?: read=(?<read>\d+))?")]
    private static partial Regex BuffersLine();

    private static async Task SeedAsync(string database)
    {
        await ViewsSeed.ActivateReleaseAsync(database, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await ViewsSeed.AddVotesAsync(database, new SeededVote('7', "2026-03-03") { BillNumber = 7 }, new SeededVote('8', "2026-03-02") { BillNumber = 8 });
        await BillsSeed.AddBillsAsync(database, new SeededBill('b', "2026-01-01") { BillNumber = 7 }, new SeededBill('d', "2026-01-01") { BillNumber = 7 });
    }

    // Vote n links bill n. The bills carry the record ids of md5(n) and md5('x' + n), as the seed of the issue fill test does.
    private async Task SeedManyBillsAsync(string database)
    {
        await ViewsSeed.ActivateReleaseAsync(database, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
        await ViewsSeed.AddVotesAsync(database, [.. Enumerable.Range(0, 16).Select(index => new SeededVote("0123456789abcdef"[index], $"2026-03-{index + 1:00}") { BillNumber = index + 1 })]);
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        foreach (var statement in $"""
            SET session_replication_role = replica;
            {PublicActionRows.ActionRevision("template", 1, "Person", "2025-02-01", 1)}
            CREATE TEMP TABLE bulk_ids AS
            SELECT n, md5(n::text) || md5('x' || n::text) AS id FROM generate_series(1, {Bills}) n;
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
            SELECT 'action', id, 1, 1, NULL FROM bulk_ids;
            INSERT INTO action_revisions
            SELECT x.*
              FROM action_revisions r, bulk_ids b,
                   LATERAL jsonb_populate_record(NULL::action_revisions,
                       to_jsonb(r) || jsonb_build_object('record_id', b.id, 'bill_number', b.n, 'run_id', NULL, 'headline', 'Headline ' || b.n)) x
             WHERE r.record_id = 'template';
            DELETE FROM action_revisions WHERE record_id = 'template';
            INSERT INTO action_revision_sponsorships (record_id, revision_number, "position", person_id, bioguide_id, role, is_original, sponsored_on)
            SELECT id, 1, 0, 'p1', 'Bp1', 'Sponsor', true, DATE '2025-02-01' FROM bulk_ids;
            ANALYZE publication_record_states, action_revisions, action_revision_sponsorships, vote_revisions;
            """.Split(";\n", StringSplitOptions.RemoveEmptyEntries))
        {
            await using var command = new NpgsqlCommand(statement, connection) { CommandTimeout = 0 };
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<string> ExplainAsync(string database)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using (var role = new NpgsqlCommand("SET ROLE civic_lens_public", connection)) await role.ExecuteNonQueryAsync();
        await using var command = new NpgsqlCommand($"EXPLAIN (ANALYZE, BUFFERS, COSTS OFF, TIMING OFF) {ListQuery}", connection);
        var lines = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) lines.Add(reader.GetString(0));
        return string.Join('\n', lines);
    }

    private static PostgresPublicationStore PostgresStore(string database) => PostgresPublicationStore.FromConnectionString(database, TestCodeVersion.Current);

    private static CollectionAttemptDbContext Context(string database) =>
        new(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);

    private static async Task ExecuteAsync(string database, string sql)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string[]> RowsAsync(string database, string sql)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(Convert.ToString(reader.GetValue(0))!);
        return [.. rows];
    }
}
