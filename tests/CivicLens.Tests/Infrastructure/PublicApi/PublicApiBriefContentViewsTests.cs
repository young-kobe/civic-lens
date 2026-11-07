using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiBriefContentViewsTests(PostgresCollection postgres)
{
    [Fact]
    public async Task PublicRoleReadsOnlyPublishedTextContentAndCitationHeadlinesFromTheirPinnedRevisions()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await PublishedBriefContentSeed.SeedAsync(connectionString);
            Assert.Equal(1L, await ScalarAsync(connectionString, "SELECT count(*) FROM brief_revision_paragraphs WHERE part = 'Suggestion' AND record_id = @id AND revision_number = 1 AND revision_number = 1", BriefFixtures.RecordId));
            Assert.Equal(BriefWorldSeeder.StatementId('3'), await TextAsync(connectionString,
                "SELECT statement_candidate_id FROM brief_revision_citations WHERE part = 'Suggestion' AND record_id = @id AND revision_number = 1", BriefFixtures.RecordId));
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = connection.CreateCommand();
            command.CommandText = """
                SET ROLE civic_lens_public;
                SELECT record_id, revision_number, release_number, position, paragraph_text, citation_ids
                  FROM public_api.brief_paragraphs ORDER BY position;
                """;
            var paragraphs = new List<string>();
            await using (var reader = await command.ExecuteReaderAsync())
                while (await reader.ReadAsync())
                    paragraphs.Add($"{reader.GetInt16(3)}|{reader.GetString(4)}|{string.Join(',', reader.GetFieldValue<string[]>(5))}");
            Assert.Equal(["0|Text paragraph one|R1", "1|Text paragraph two|R2,R3"], paragraphs);

            await using var citationsCommand = connection.CreateCommand();
            citationsCommand.CommandText = """
                SET ROLE civic_lens_public;
                SELECT citation_id || '|' || kind || '|' || target_record_id || '|' || target_revision_number || '|' || headline
                  FROM public_api.brief_citations ORDER BY position;
                """;
            var citations = new List<string>();
            await using (var reader = await citationsCommand.ExecuteReaderAsync())
                while (await reader.ReadAsync()) citations.Add(reader.GetString(0));
            Assert.Equal(
            [
                $"R1|statement|{BriefFixtures.CitedStatement}|1|Pinned statement headline",
                $"R2|documentChange|{BriefFixtures.CitedDocumentChange}|1|Pinned document headline",
                $"R3|action|{BriefFixtures.CitedAction}|1|Bill 7"
            ], citations);

            await using var denied = connection.CreateCommand();
            denied.CommandText = "SET ROLE civic_lens_public; SELECT * FROM brief_revision_paragraphs;";
            await Assert.ThrowsAsync<PostgresException>(() => denied.ExecuteNonQueryAsync());
            await using var deniedSuggestion = connection.CreateCommand();
            deniedSuggestion.CommandText = "SET ROLE civic_lens_public; SELECT * FROM brief_revision_citations;";
            await Assert.ThrowsAsync<PostgresException>(() => deniedSuggestion.ExecuteNonQueryAsync());
        }
        finally
        {
            await postgres.DropDatabaseAsync(connectionString);
        }
    }

    [Fact]
    public async Task AChildViewHidesContentWhenTheParentBriefViewHidesItsRevision()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await PublishedBriefContentSeed.SeedAsync(connectionString);
            await BriefWorldSeeder.RunAsync(connectionString,
                "UPDATE brief_revisions SET week_start = NULL, window_start_utc_ticks = NULL, window_end_utc_ticks = NULL, first_release_number = NULL, " +
                "cutoff_release_number = NULL, fingerprint = NULL, headline = NULL, text_origin = NULL, coverage_considered = NULL, coverage_sent_to_model = NULL " +
                "WHERE record_id = @id AND revision_number = 1",
                ("id", BriefFixtures.RecordId));
            Assert.Equal(0L, await ScalarAsync(connectionString, "SELECT count(*) FROM public_api.briefs WHERE record_id = @id", BriefFixtures.RecordId));
            Assert.Equal(0L, await ScalarAsync(connectionString, "SELECT count(*) FROM public_api.brief_paragraphs WHERE record_id = @id", BriefFixtures.RecordId));
            Assert.Equal(0L, await ScalarAsync(connectionString, "SELECT count(*) FROM public_api.brief_citations WHERE record_id = @id", BriefFixtures.RecordId));
        }
        finally
        {
            await postgres.DropDatabaseAsync(connectionString);
        }
    }



    [Fact]
    public async Task DowngradeRemovesTheAddedViewsAndTheirOwnerColumnGrants()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
            var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, "20261107040000_PublicApiBriefContent") < 0);
            await db.GetService<IMigrator>().MigrateAsync(previous);
            Assert.Equal(0L, await ScalarAsync(connectionString, "SELECT count(*) FROM information_schema.views WHERE table_schema = 'public_api' AND table_name IN ('brief_paragraphs','brief_citations')", null));
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT has_column_privilege('civic_lens_public_owner', 'brief_revision_paragraphs', 'paragraph_text', 'SELECT')
                    OR has_column_privilege('civic_lens_public_owner', 'brief_revision_citations', 'statement_candidate_id', 'SELECT')
                    OR has_column_privilege('civic_lens_public_owner', 'brief_revision_paragraph_citations', 'citation_id', 'SELECT')
                    OR has_column_privilege('civic_lens_public_owner', 'document_change_revisions', 'headline', 'SELECT')
                """, connection);
            Assert.False((bool)(await command.ExecuteScalarAsync())!);
        }
        finally { await postgres.DropDatabaseAsync(connectionString); }
    }

    private static async Task<string> TextAsync(string connectionString, string sql, string id)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", id);
        return (string)(await command.ExecuteScalarAsync())!;
    }

    private static async Task<long> ScalarAsync(string connectionString, string sql, string? id)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        if (id is not null) command.Parameters.AddWithValue("id", id);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }
}
