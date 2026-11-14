using System.Diagnostics;
using CivicLens.PublicApi.Ingest;
using CivicLens.PublicApi.Records;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;
using Xunit.Abstractions;

namespace CivicLens.Tests.PublicApi.Ingest;

/// <summary>
/// The entry read of a large batch stays inside the statement timeout of <c>civic_lens_public</c>.
/// Each query starts from the keys and reads its view for each key with <see cref="RevisionKeys.Match"/>. A plain <c>IN</c> filter or a plain join to <c>release_officials</c> makes the read quadratic, and the read then fails at the timeout.
/// </summary>
[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class TypedEntryReaderScaleTests(PostgresCollection postgres, ITestOutputHelper output)
{
    // With the template, the active release holds 21,944 revisions and 541 officials, as release 1096 of production does.
    private const int Release = 1096;
    private const int Copies = 21943;
    private const int Officials = 540;

    [Fact]
    public async Task TheEntriesOfTheProductionSizeOfRelease1096WithTheirOfficialsIssuesAndAreasReadInsideTheTimeoutOfThePublicRole()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            var template = PublishedSamples.Statement('1', "Rent cap", issues: ["housing", "budget"], areas: ["economics"]);
            await new TypedReleaseSite(database).PublishAsync(1, template);
            await CopyAsync(database, template.Entry.RecordId);
            await using var views = NpgsqlDataSource.Create(await ApiHost.LogInPublicRoleAsync(database));
            ListedRecord[] listed;
            await using (var read = (await new TypedReleaseSource(new RecordSnapshot(views)).ReadActiveAsync(CancellationToken.None))!)
                listed = [.. read.Release.Records];
            var (connection, snapshot) = await new RecordSnapshot(views).OpenAsync(CancellationToken.None);
            await using (connection)
            await using (snapshot)
            {
                var clock = Stopwatch.StartNew();
                var entries = await new TypedEntryReader(snapshot).ReadAsync(new RevisionKeys(listed), Release, CancellationToken.None);
                output.WriteLine($"{listed.Length} revisions read in {clock.ElapsedMilliseconds} ms.");

                Assert.Equal(Copies + 1, listed.Length);
                Assert.Equal(Copies + 1, entries.Count);
                Assert.All(entries.Values, entry =>
                {
                    Assert.Equal(["o1=Official o1"], entry.Officials.Select(official => official.Id + "=" + official.Name));
                    Assert.Equal(template.Entry.MatchedOfficialId, entry.MatchedOfficialId);
                    Assert.Equal(["housing", "budget"], entry.IssueIds);
                    Assert.Equal(["economics"], entry.PolicyAreaIds);
                    Assert.Equal(template.Entry.FirstPublishedAtUtc, entry.FirstPublishedAtUtc);
                });
            }
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    private static readonly string[] Tables =
        ["publication_record_states", "published_records", "published_entries", "published_entry_officials", "published_entry_issues", "published_entry_policy_areas"];

    // Each copy repeats the entry rows of the template under the record ID md5(n) || md5('x' || n), as the seed of the vote list plan test does.
    // A statement entry repeats its record ID as statement_candidate_id. The seed then gives the data the shape of production:
    // many releases, record states that start in different releases, some closed states, and more officials in the active release.
    private static async Task CopyAsync(string database, string template)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        string[] copies = [.. await Task.WhenAll(Tables.Select(table => CopySqlAsync(database, table, template)))];
        foreach (var statement in (string[])[
            "SET session_replication_role = replica",
            $"CREATE TEMP TABLE copy_ids AS SELECT md5(n::text) || md5('x' || n::text) AS id FROM generate_series(1, {Copies}) n",
            .. copies,
            $"""
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active, code_version)
            SELECT n, lpad(n::text, 6, '0') || '-' || md5(n::text), actor_subject, published_at_utc_ticks + n, record_count, false, code_version
              FROM publication_releases, generate_series(2, {Release}) n WHERE release_number = 1
            """,
            "UPDATE publication_releases SET is_active = false WHERE release_number = 1",
            $"UPDATE publication_releases SET is_active = true WHERE release_number = {Release}",
            $"""
            INSERT INTO release_officials (release_number, official_id, name)
            SELECT {Release}, official_id, name FROM release_officials WHERE release_number = 1
             UNION ALL SELECT {Release}, 'x' || n, 'Official x' || n FROM generate_series(1, {Officials}) n
            """,
            $"UPDATE publication_record_states SET first_release = 1 + abs(hashtext(record_id)) % {Release}",
            """
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
            SELECT kind, record_id, 1, revision_number, first_release - 1 FROM publication_record_states
             WHERE first_release > 1 AND abs(hashtext(record_id)) % 8 = 0
            """,
            $"ANALYZE {string.Join(", ", Tables)}, publication_releases, release_officials"])
        {
            await using var command = new NpgsqlCommand(statement, connection) { CommandTimeout = 0 };
            await command.ExecuteNonQueryAsync();
        }
    }

    // The copy names the stored columns only, because published_entries has a generated column.
    private static async Task<string> CopySqlAsync(string database, string table, string template)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT string_agg(quote_ident(column_name), ', ' ORDER BY ordinal_position) FROM information_schema.columns
             WHERE table_schema = 'public' AND table_name = @table AND is_generated = 'NEVER'
            """, connection);
        command.Parameters.AddWithValue("table", table);
        var columns = (string)(await command.ExecuteScalarAsync())!;
        var candidate = table == "published_entries" ? ", 'statement_candidate_id', c.id" : "";
        return $"""
            INSERT INTO {table} ({columns})
            SELECT {string.Join(", ", columns.Split(", ").Select(column => "x." + column))} FROM {table} t, copy_ids c,
                   LATERAL jsonb_populate_record(NULL::{table}, to_jsonb(t) || jsonb_build_object('record_id', c.id{candidate})) x
             WHERE t.record_id = '{template}'
            """;
    }
}
