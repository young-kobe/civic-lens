using CivicLens.Host.Publication;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiViewsTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Role = "civic_lens_public";
    private const string Owner = "civic_lens_public_owner";
    private const string LoginPassword = "test-only-password";

    private static readonly string[] BaseTables =
        ["vote_revisions", "vote_member_list_entries", "vote_revision_issues", "publication_releases", "publication_record_states", "vote_drafts", "vote_member_lists"];

    private string connectionString = null!;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        await ExecuteAsync($"ALTER ROLE {Role} LOGIN PASSWORD '{LoginPassword}'");
    }

    // The role belongs to the cluster, so each test puts it back to the state that the migration leaves.
    public async Task DisposeAsync()
    {
        await ExecuteAsync($"ALTER ROLE {Role} NOLOGIN PASSWORD NULL");
        await postgres.DropDatabaseAsync(connectionString);
    }

    [Fact]
    public async Task TheRoleReadsNoBaseTableAndWritesNothingBecauseItHoldsOnlyTheViewGrants()
    {
        await ExecuteAsync(ReadOnlyGrantsScript());
        await using var connection = await OpenPublicAsync();
        // The session can lift the read-only default itself, so the grants must hold alone.
        await using (var lift = new NpgsqlCommand("SET default_transaction_read_only = off", connection)) await lift.ExecuteNonQueryAsync();
        foreach (var table in BaseTables)
            await AssertDeniedAsync(connection, $"SELECT * FROM public.{table}");
        await AssertDeniedAsync(connection, "SELECT count(*) FROM public.vote_revisions");
        await AssertDeniedAsync(connection, "SELECT * FROM readonly_access.revisions('vote_revisions')");
        await AssertDeniedAsync(connection, "INSERT INTO public.publication_releases (release_number) VALUES (9)");
        await AssertDeniedAsync(connection, "UPDATE public.vote_revisions SET headline = 'x'");
        await AssertDeniedAsync(connection, "DELETE FROM public.publication_record_states");
        foreach (var view in new[] { "active_records", "votes", "vote_members", "vote_issues" })
        {
            // A view with a join or a group cannot take a write at all, so Postgres refuses with 55000 before it checks a privilege.
            await AssertDeniedAsync(connection, $"DELETE FROM public_api.{view}", "55000");
            await AssertDeniedAsync(connection, $"UPDATE public_api.{view} SET record_id = 'x'", "55000");
        }
    }

    [Fact]
    public async Task EveryViewColumnIsInTheCheckedInListBecauseANewColumnNeedsAVisibleEdit()
    {
        var path = Path.Combine(RepositoryRoot.FullName, "docs", "architecture", "public-api-columns.txt");
        var listed = File.ReadAllLines(path).Where(line => line.Length > 0 && line[0] != '#').Order(StringComparer.Ordinal).ToArray();
        var found = (await QueryAsync("""
            SELECT table_name || ' ' || column_name FROM information_schema.columns WHERE table_schema = 'public_api'
            """, connectionString)).Order(StringComparer.Ordinal).ToArray();
        Assert.Equal(listed, found);
    }

    [Fact]
    public async Task EveryObjectOfThePublicApiSchemaIsAViewOfTheOwnerWithOneSelectGrantToTheRoleAndNoOtherGrantee()
    {
        var objects = await QueryAsync("""
            SELECT c.relname || ' ' || c.relkind::text || ' ' || pg_get_userbyid(c.relowner) || ' '
                   || coalesce((SELECT string_agg(pg_get_userbyid(g.grantee) || ':' || g.privilege_type, ',' ORDER BY g.grantee)
                                  FROM aclexplode(c.relacl) g WHERE g.grantee <> c.relowner), 'none') || ' '
                   || coalesce(c.reloptions::text, 'none')
              FROM pg_class c JOIN pg_namespace n ON n.oid = c.relnamespace WHERE n.nspname = 'public_api' ORDER BY c.relname
            """, connectionString);
        Assert.Equal(
        [
            $"active_records v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"vote_issues v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"vote_members v {Owner} {Role}:SELECT {{security_barrier=true}}",
            $"votes v {Owner} {Role}:SELECT {{security_barrier=true}}"
        ], objects);
        Assert.Empty(await QueryAsync("SELECT proname FROM pg_proc p JOIN pg_namespace n ON n.oid = p.pronamespace WHERE n.nspname = 'public_api'", connectionString));
        var schema = await QueryAsync("""
            SELECT pg_get_userbyid(n.nspowner) || ' ' || (SELECT string_agg(coalesce(nullif(g.grantee, 0)::regrole::text, 'PUBLIC') || ':' || g.privilege_type, ',')
                                                           FROM aclexplode(n.nspacl) g WHERE g.grantee <> n.nspowner)
              FROM pg_namespace n WHERE n.nspname = 'public_api'
            """, connectionString);
        Assert.Equal([$"{Owner} {Role}:USAGE"], schema);
    }

    [Fact]
    public async Task TheRoleIsReadOnlyShortLivedAndLimitedAndTheMigrationLeavesItWithoutLoginOrPassword()
    {
        await ExecuteAsync($"ALTER ROLE {Role} NOLOGIN PASSWORD NULL");
        var role = await QueryAsync($"""
            SELECT rolcanlogin || ' ' || rolconnlimit || ' ' || (rolpassword IS NULL) || ' ' || rolsuper || ' ' || rolcreaterole || ' ' || rolcreatedb
              FROM pg_authid WHERE rolname = '{Role}'
            """, connectionString);
        Assert.Equal(["false 5 true false false false"], role);
        var settings = await QueryAsync($"SELECT unnest(setconfig) FROM pg_db_role_setting WHERE setrole = '{Role}'::regrole ORDER BY 1", connectionString);
        Assert.Equal(["default_transaction_read_only=on", "idle_in_transaction_session_timeout=10s", "statement_timeout=2s"], settings);
        var owner = await QueryAsync($"SELECT rolcanlogin FROM pg_roles WHERE rolname = '{Owner}'", connectionString);
        Assert.Equal(["False"], owner);
    }

    [Fact]
    public async Task TheLabelWordingInTheViewMatchesTheWordingThatThePagesShow()
    {
        var definition = Assert.Single(await QueryAsync("SELECT pg_get_viewdef('public_api.votes'::regclass)", connectionString));
        Assert.Contains($"'{PublishedRecordLabels.ModelWording}'", definition, StringComparison.Ordinal);
        Assert.Contains($"'{PublishedRecordLabels.CodeWording}'", definition, StringComparison.Ordinal);
    }

    [Fact]
    public async Task OnlyRecordsOfTheActiveReleaseAppearAndARollbackMovesTheViewsAtOnce()
    {
        await SeedAsync();
        Assert.Equal(["a", "c", "d"], await PublicAsync("SELECT DISTINCT record_id FROM public_api.votes ORDER BY 1"));
        Assert.Equal(["a 2 2", "c 1 2", "d 1 2"], await PublicAsync("SELECT record_id || ' ' || revision_number || ' ' || release_number FROM public_api.votes ORDER BY 1"));
        Assert.Equal(["2"], await PublicAsync("SELECT DISTINCT release_number FROM public_api.active_records"));
        Assert.Equal(["a", "c"], await PublicAsync("SELECT DISTINCT record_id FROM public_api.vote_members ORDER BY 1"));
        Assert.Equal(["d"], await PublicAsync("SELECT DISTINCT record_id FROM public_api.vote_issues"));

        await ExecuteAsync("UPDATE publication_releases SET is_active = false; UPDATE publication_releases SET is_active = true WHERE release_number = 1");

        Assert.Equal(["a 1 1", "b 1 1", "d 1 1"], await PublicAsync("SELECT record_id || ' ' || revision_number || ' ' || release_number FROM public_api.votes ORDER BY 1"));
        Assert.Equal(["a", "b"], await PublicAsync("SELECT DISTINCT record_id FROM public_api.vote_members ORDER BY 1"));

        await ExecuteAsync("UPDATE publication_releases SET is_active = false");
        Assert.Empty(await PublicAsync("SELECT record_id FROM public_api.votes"));
        Assert.Empty(await PublicAsync("SELECT record_id FROM public_api.active_records"));
    }

    [Fact]
    public async Task VoteCountsEqualTheMemberRowsAndEveryAiOrCodeRowHasItsLabel()
    {
        await SeedAsync();
        Assert.Equal(["a 2 2 1 2 7"], await PublicAsync("SELECT record_id || ' ' || yea || ' ' || nay || ' ' || present || ' ' || not_voting || ' ' || (yea + nay + present + not_voting) FROM public_api.votes WHERE record_id = 'a'"));
        Assert.Empty(await PublicAsync("""
            SELECT v.record_id FROM public_api.votes v
             WHERE v.yea + v.nay + v.present + v.not_voting <> (SELECT count(*) FROM public_api.vote_members m WHERE m.record_id = v.record_id AND m.revision_number = v.revision_number)
            """));
        Assert.Empty(await PublicAsync("SELECT record_id FROM public_api.votes WHERE text_origin IN ('Model', 'Code') AND label IS NULL"));
        Assert.Equal(
        [
            $"a Code {PublishedRecordLabels.CodeWording}", $"c Person none", $"d Model {PublishedRecordLabels.ModelWording}"
        ], await PublicAsync("SELECT record_id || ' ' || text_origin || ' ' || coalesce(label, 'none') FROM public_api.votes ORDER BY 1"));
        Assert.Equal(["0 0 0 0"], await PublicAsync("SELECT yea || ' ' || nay || ' ' || present || ' ' || not_voting FROM public_api.votes WHERE record_id = 'd'"));
    }

    private async Task SeedAsync()
    {
        await ExecuteAsync("""
            SET session_replication_role = replica;
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active) VALUES
              (1, '000001-00000000000000000000000000000001', 'test', 1, 3, false),
              (2, '000002-00000000000000000000000000000002', 'test', 2, 3, true);
            -- a: revision 1 in release 1 only, revision 2 from release 2. b: release 1 only. c: from release 2. d: both.
            INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release) VALUES
              ('vote', 'a', 1, 1, 1), ('vote', 'a', 2, 2, NULL), ('vote', 'b', 1, 1, 1), ('vote', 'c', 2, 1, NULL), ('vote', 'd', 1, 1, NULL),
              ('statement', 'a', 1, 1, NULL);
            """ + string.Join('\n', new[]
        {
            Revision("a", 1, "Person", "1111111111111111111111111111111111111111111111111111111111111111"), Revision("a", 2, "Code", "2222222222222222222222222222222222222222222222222222222222222222"), Revision("b", 1, "Model", "3333333333333333333333333333333333333333333333333333333333333333"),
            Revision("c", 1, "Person", "4444444444444444444444444444444444444444444444444444444444444444"), Revision("d", 1, "Model", "5555555555555555555555555555555555555555555555555555555555555555")
        }) + """
            INSERT INTO vote_member_lists (list_id, member_count, created_at_utc_ticks) VALUES ('1111111111111111111111111111111111111111111111111111111111111111', 1, 1), ('2222222222222222222222222222222222222222222222222222222222222222', 8, 1), ('3333333333333333333333333333333333333333333333333333333333333333', 1, 1), ('4444444444444444444444444444444444444444444444444444444444444444', 1, 1), ('5555555555555555555555555555555555555555555555555555555555555555', 1, 1);
            """ + string.Join('\n', new[]
        {
            Entry("1111111111111111111111111111111111111111111111111111111111111111", 0, "Yea"), Entry("3333333333333333333333333333333333333333333333333333333333333333", 0, "Nay"), Entry("4444444444444444444444444444444444444444444444444444444444444444", 0, "Present"), Entry("5555555555555555555555555555555555555555555555555555555555555555", 0, "Yea"),
            Entry("2222222222222222222222222222222222222222222222222222222222222222", 0, "Yea"), Entry("2222222222222222222222222222222222222222222222222222222222222222", 1, "Aye"), Entry("2222222222222222222222222222222222222222222222222222222222222222", 2, "Nay"), Entry("2222222222222222222222222222222222222222222222222222222222222222", 3, "No"), Entry("2222222222222222222222222222222222222222222222222222222222222222", 4, "Present"),
            Entry("2222222222222222222222222222222222222222222222222222222222222222", 5, "Not Voting"), Entry("2222222222222222222222222222222222222222222222222222222222222222", 6, "Not Voting")
        }) + """
            INSERT INTO vote_revision_issues (record_id, revision_number, issue_id, position, is_tracked) VALUES ('d', 1, 'health', 0, true), ('b', 1, 'tax', 0, false);
            """);
        // Revision d has an empty member list row, so it has no entries: counts stay at zero.
        await ExecuteAsync("DELETE FROM vote_member_list_entries WHERE list_id = '5555555555555555555555555555555555555555555555555555555555555555'");
    }

    private static string Revision(string record, int revision, string origin, string list) => $"""
        INSERT INTO vote_revisions (record_id, revision_number, author_subject, author_kind, revision_json, created_at_utc_ticks,
            headline, summary, text_origin, source_id, vote_source, chamber, congress, session_number, roll_call_number, candidate_id,
            candidate_update_utc_ticks, voted_on, voted_on_citation_extraction_id, voted_on_citation_start, voted_on_citation_length,
            kind, member_list_id, issue_revision_id, issue_candidates_recorded, bill_type, bill_number)
        VALUES ('{record}', {revision}, 'test', 'Person', 'x', 1, 'Headline {record}', 'Summary {record}', '{origin}', 'src', 'CongressGov', 'House', 119, 1, 7,
            'cand', 1, '2026-01-02', 'ext', 0, 1, 'Passage', '{list}', '{new string('a', 64)}', true, 'HR', 1);
        """;

    private static string Entry(string list, int position, string cast) => $"""
        INSERT INTO vote_member_list_entries (list_id, "position", person_id, bioguide_id, vote_cast, citation_extraction_id, citation_start, citation_length)
        VALUES ('{list}', {position}, 'p{position}', 'B{position:000000}', '{cast}', 'ext', 0, 1);
        """;

    private static string ReadOnlyGrantsScript() => string.Join('\n',
        File.ReadAllLines(Path.Combine(RepositoryRoot.FullName, "deploy", "scripts", "readonly-grants.sql")).Where(line => !line.StartsWith('\\')));

    private static async Task AssertDeniedAsync(NpgsqlConnection connection, string sql, string sqlState = "42501")
    {
        await using var command = new NpgsqlCommand(sql, connection);
        var error = await Assert.ThrowsAnyAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
        Assert.Equal(sqlState, error.SqlState);
    }

    private async Task<NpgsqlConnection> OpenPublicAsync()
    {
        var builder = new NpgsqlConnectionStringBuilder(connectionString) { Username = Role, Password = LoginPassword };
        var connection = new NpgsqlConnection(builder.ConnectionString);
        await connection.OpenAsync();
        return connection;
    }

    private async Task<string[]> PublicAsync(string sql)
    {
        await using var connection = await OpenPublicAsync();
        return await ReadAsync(connection, sql);
    }

    private static async Task<string[]> QueryAsync(string sql, string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        return await ReadAsync(connection, sql);
    }

    private static async Task<string[]> ReadAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(Convert.ToString(reader.GetValue(0))!);
        return [.. rows];
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
