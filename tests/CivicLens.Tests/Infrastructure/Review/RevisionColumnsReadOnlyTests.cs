using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Review;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class RevisionColumnsReadOnlyTests(PostgresCollection postgres) : IAsyncLifetime
{
    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateMigratedDatabaseAsync();

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    // The columns hide by name, so each text column of a revision must match the pattern and no other new column may.
    [Theory]
    [InlineData("vote_revisions,vote_revision_facts,vote_revision_issues,vote_member_lists,vote_member_list_entries",
        "author_subject,headline,summary,fact_text,suggestion_headline,suggestion_summary,suggestion_run_id,suggestion_fingerprint")]
    [InlineData("action_revisions,action_revision_issues,action_revision_sponsorships,action_revision_sponsorship_citations",
        "author_subject,revision_json,headline,summary,suggestion_headline,suggestion_summary,suggestion_run_id,suggestion_fingerprint")]
    public async Task EveryTextColumnOfARevisionIsHiddenFromTheReadOnlyRoleAndTheStructureColumnsAreNot(string tables, string hiddenColumns)
    {
        var expectedHidden = hiddenColumns.Split(',');
        var hidden = ReadOnlyGrants.HiddenColumn();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(
            "SELECT column_name FROM information_schema.columns WHERE table_schema = 'public' AND table_name = ANY (@tables)", connection);
        command.Parameters.AddWithValue("tables", tables.Split(','));
        var columns = new List<string>();
        await using (var reader = await command.ExecuteReaderAsync())
            while (await reader.ReadAsync()) columns.Add(reader.GetString(0));

        Assert.All(expectedHidden, column => Assert.Contains(column, columns));
        Assert.All(columns, column => Assert.Equal(expectedHidden.Contains(column), hidden.IsMatch(column)));
    }

    // A kind that dropped revision_json and a kind that keeps it return the same one column, and neither shows the author.
    [Fact]
    public async Task TheRevisionFunctionReturnsTheRowWithoutTheAuthorForATableWithoutJsonAndForATableWithJsonAndTheOwnerReadsBoth()
    {
        var script = GrantsScript();
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, $$"""
            SET session_replication_role = replica;
            INSERT INTO vote_revisions (record_id, revision_number, author_subject, author_kind, run_id, created_at_utc_ticks,
                headline, summary, text_origin, source_id, vote_source, chamber, congress, session_number, roll_call_number, candidate_id,
                candidate_update_utc_ticks, voted_on, voted_on_citation_extraction_id, voted_on_citation_start, voted_on_citation_length, kind,
                member_list_id, issue_revision_id, issue_candidates_recorded, result)
            VALUES ('{{new string('a', 64)}}', 1, 'auth0|vote-author', 'Person', NULL, 100,
                'Vote headline', 'Summary', 'Person', 'voteview-senate', 'Voteview', 'Senate', 119, 1, 7, 'c1',
                1, DATE '2026-01-02', 'e1', 0, 5, 'Passage', '{{new string('1', 64)}}', '{{new string('2', 64)}}', true, 'Passed');
            INSERT INTO action_revisions (record_id, revision_number, author_subject, author_kind, revision_json, created_at_utc_ticks)
            VALUES ('{{new string('b', 64)}}', 1, 'auth0|action-author', 'Person', '{"authorSubject":"auth0|action-author","title":"Kept"}', 100);
            {{script}}
            SET ROLE civic_lens_readonly
            """);

        var vote = await ScalarAsync(connection, "SELECT row_data::text FROM readonly_access.revisions('vote_revisions')");
        var action = await ScalarAsync(connection, "SELECT row_data::text FROM readonly_access.revisions('action_revisions')");

        Assert.Contains("Vote headline", vote, StringComparison.Ordinal);
        Assert.DoesNotContain("auth0|vote-author", vote, StringComparison.Ordinal);
        Assert.DoesNotContain("author_subject", vote, StringComparison.Ordinal);
        Assert.Contains("\"title\": \"Kept\"", action, StringComparison.Ordinal);
        Assert.DoesNotContain("auth0|action-author", action, StringComparison.Ordinal);
        await ExecuteAsync(connection, "RESET ROLE");
        Assert.Equal("true", await ScalarAsync(connection, "SELECT has_table_privilege('civic_lens_readonly_owner', 'vote_revisions', 'SELECT')::text"));
    }

    // Production already holds the old function with two columns, and CREATE OR REPLACE cannot change a return type.
    [Fact]
    public async Task TheGrantsScriptReplacesAnOldRevisionFunctionThatReturnedTwoColumnsBecauseProductionAlreadyHoldsIt()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await ExecuteAsync(connection, $$"""
            SET session_replication_role = replica;
            CREATE SCHEMA IF NOT EXISTS readonly_access;
            CREATE FUNCTION readonly_access.revisions(table_name text) RETURNS TABLE (row_data jsonb, revision jsonb)
            LANGUAGE sql AS 'SELECT NULL::jsonb, NULL::jsonb WHERE false';
            INSERT INTO vote_revisions (record_id, revision_number, author_subject, author_kind, run_id, created_at_utc_ticks,
                headline, summary, text_origin, source_id, vote_source, chamber, congress, session_number, roll_call_number, candidate_id,
                candidate_update_utc_ticks, voted_on, voted_on_citation_extraction_id, voted_on_citation_start, voted_on_citation_length, kind,
                member_list_id, issue_revision_id, issue_candidates_recorded, result)
            VALUES ('{{new string('a', 64)}}', 1, 'auth0|vote-author', 'Person', NULL, 100,
                'Vote headline', 'Summary', 'Person', 'voteview-senate', 'Voteview', 'Senate', 119, 1, 7, 'c1',
                1, DATE '2026-01-02', 'e1', 0, 5, 'Passage', '{{new string('1', 64)}}', '{{new string('2', 64)}}', true, 'Passed');
            {{GrantsScript()}}
            SET ROLE civic_lens_readonly
            """);

        var vote = await ScalarAsync(connection, "SELECT row_data::text FROM readonly_access.revisions('vote_revisions')");

        Assert.Contains("Vote headline", vote, StringComparison.Ordinal);
    }

    private static string GrantsScript() => string.Join('\n', File.ReadAllLines(Path.Combine(RepositoryRoot.FullName, "deploy", "scripts", "readonly-grants.sql"))
        .Where(line => !line.StartsWith('\\')));

    private static async Task ExecuteAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string> ScalarAsync(NpgsqlConnection connection, string sql)
    {
        await using var command = new NpgsqlCommand(sql, connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
