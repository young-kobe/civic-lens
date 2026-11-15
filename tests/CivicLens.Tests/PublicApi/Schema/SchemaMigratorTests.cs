using CivicLens.PublicApi.Schema;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.PublicApi.Schema;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class SchemaMigratorTests(PostgresCollection postgres)
{
    [Fact]
    public async Task TwoServicesThatStartTogetherApplyEachFileOnceAndRecordItInSchemaMigrations()
    {
        var connection = await postgres.CreateEmptyDatabaseAsync();
        try
        {
            await using var first = NpgsqlDataSource.Create(connection);
            await using var second = NpgsqlDataSource.Create(connection);

            await Task.WhenAll(new SchemaMigrator(first).ApplyAsync(CancellationToken.None), new SchemaMigrator(second).ApplyAsync(CancellationToken.None));
            await new SchemaMigrator(first).ApplyAsync(CancellationToken.None);

            await using var command = first.CreateCommand("SELECT version || ':' || name FROM schema_migrations");
            Assert.Equal("1:001_search_index", await command.ExecuteScalarAsync());
        }
        finally
        {
            await postgres.DropDatabaseAsync(connection);
        }
    }

    [Fact]
    public async Task TheHideCutBillLinesFileClearsTheLineAndTheSnippetOfAStoredRowThatEndsWithTheCutMarkAndKeepsAWholeLine()
    {
        var connection = await postgres.CreateEmptyDatabaseAsync();
        try
        {
            await using var database = NpgsqlDataSource.Create(connection);
            await new SchemaMigrator(database).ApplyAsync(CancellationToken.None);
            await Execute(database, """
                INSERT INTO releases (release_number, published_at_utc) VALUES (1, now());
                INSERT INTO records (record_id, revision_number, kind, headline, first_published_at_utc, officials, official_ids, issue_ids, policy_area_ids,
                                     bill_line, summary, search, first_release)
                SELECT id, 1, 'action', 'H', now(), '[]', '{}', '{}', '{}', line, line, to_tsvector('Body'), 1
                  FROM (VALUES ('cut', 'The Department reports on sanctions that are' || chr(8230)), ('whole', 'The Department reports.')) AS rows (id, line);
                DELETE FROM schema_migrations WHERE name = '007_hide_cut_bill_lines';
                """);

            await new SchemaMigrator(database).ApplyAsync(CancellationToken.None);

            await using var command = database.CreateCommand("SELECT string_agg(record_id || ':' || coalesce(bill_line, '-') || ':' || coalesce(summary, '-'), '|' ORDER BY record_id) FROM records");
            Assert.Equal("cut:-:-|whole:The Department reports.:The Department reports.", await command.ExecuteScalarAsync());
        }
        finally
        {
            await postgres.DropDatabaseAsync(connection);
        }
    }

    private static async Task Execute(NpgsqlDataSource database, string sql)
    {
        await using var command = database.CreateCommand(sql);
        await command.ExecuteNonQueryAsync();
    }
}
