using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Statements;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiStatementListsMigrationTests(PostgresCollection postgres)
{
    [Fact]
    public async Task EvidenceViewsKeepTheCandidateExtractionAndActiveRevisionAndExcludeIneligibleStatements()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await ViewsSeed.ActivateReleaseAsync(database, 2, new DateTimeOffset(2026, 6, 9, 0, 0, 0, TimeSpan.Zero));
            var active = new SeededStatement('a', "2026-03-02", ["official-a"], ["health"], "Person", "Person", AddUnpublishedRevision: true);
            var deactivated = new SeededStatement('b', "2026-03-01", ["official-b"], ["health"], "Person", "Person") { LastRelease = 1 };
            var missingMetadata = new SeededStatement('c', "2026-02-28", ["official-c"], ["health"], "Person", "Person", HasMetadata: false);
            var missingSpeaker = new SeededStatement('d', "2026-02-27", [], ["health"], "Person", "Person");
            await StatementsSeed.AddAsync(database, active, deactivated, missingMetadata, missingSpeaker);

            const string sourceText = "Officials said here on Friday.";
            const string extractionId = "1111111111111111111111111111111111111111111111111111111111111111";
            var quoteStart = sourceText.IndexOf("said here", StringComparison.Ordinal);
            var dateStart = sourceText.IndexOf("Friday", StringComparison.Ordinal);
            await ViewsSeed.ExecuteAsync(database, $"""
                SET session_replication_role = replica;
                INSERT INTO collection_attempts (attempt_id, source_id, requested_url, final_url, observed_at_utc_ticks,
                    outcome, has_response, response_status_code, response_content_encodings, has_sent_validators,
                    capture_sha256, elapsed_milliseconds)
                VALUES ('attempt-pinned', 'source', 'https://feed.example/rss', 'https://cdn.example/rss', 123,
                    'captured', true, 200, ARRAY[]::text[], false, REPEAT('a', 64), 0),
                       ('attempt-newer', 'source', 'https://newer.example/page', 'https://newer-cdn.example/page', 456,
                    'captured', true, 200, ARRAY[]::text[], false, REPEAT('b', 64), 0);
                INSERT INTO document_extractions (extraction_id, attempt_id, extraction_version, text, text_sha256)
                VALUES ('{extractionId}', 'attempt-pinned', 'test', '{sourceText}', REPEAT('c', 64)),
                       (REPEAT('2', 64), 'attempt-newer', 'test', 'replacement text', REPEAT('d', 64));
                INSERT INTO statement_candidates (candidate_id, source_id, url, extraction_id, attempt_id, created_at_utc_ticks)
                VALUES ('{active.RecordId}', 'source', 'https://article.example/story', '{extractionId}', 'attempt-pinned', 1),
                       ('{deactivated.RecordId}', 'source', 'https://article.example/old', '{extractionId}', 'attempt-pinned', 1),
                       ('{missingMetadata.RecordId}', 'source', 'https://article.example/metadata', '{extractionId}', 'attempt-pinned', 1),
                       ('{missingSpeaker.RecordId}', 'source', 'https://article.example/speaker', '{extractionId}', 'attempt-pinned', 1);
                UPDATE statement_revisions
                   SET statement_date_evidence_extraction_id = '{extractionId}',
                       statement_date_evidence_start = {dateStart}, statement_date_evidence_length = 6
                 WHERE candidate_id = '{active.RecordId}' AND revision_number = 1;
                INSERT INTO statement_revision_quotations (candidate_id, revision_number, position, quotation_extraction_id,
                    quotation_start, quotation_length, kind)
                VALUES ('{active.RecordId}', 1, 0, '{extractionId}', {quoteStart}, 9, 'DirectQuotation'),
                       ('{active.RecordId}', 2, 0, REPEAT('2', 64), 0, 11, 'ReleaseText'),
                       ('{deactivated.RecordId}', 1, 0, '{extractionId}', {quoteStart}, 9, 'DirectQuotation'),
                       ('{missingMetadata.RecordId}', 1, 0, '{extractionId}', {quoteStart}, 9, 'DirectQuotation'),
                       ('{missingSpeaker.RecordId}', 1, 0, '{extractionId}', {quoteStart}, 9, 'DirectQuotation');
                """);

            await using var connection = new NpgsqlConnection(database);
            await connection.OpenAsync();
            await using (var role = new NpgsqlCommand("SET ROLE civic_lens_public", connection)) await role.ExecuteNonQueryAsync();
            await using (var source = new NpgsqlCommand("""
                SELECT revision_number, extraction_id, url, observed_at_utc_ticks, text, article_url,
                       statement_date_evidence_extraction_id, statement_date_evidence_start, statement_date_evidence_length
                  FROM public_api.statement_sources WHERE record_id = @id
                """, connection))
            {
                source.Parameters.AddWithValue("id", active.RecordId);
                await using var reader = await source.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(1, reader.GetInt32(0));
                Assert.Equal(extractionId, reader.GetString(1));
                Assert.Equal("https://feed.example/rss", reader.GetString(2));
                Assert.Equal(123L, reader.GetInt64(3));
                Assert.Equal(sourceText, reader.GetString(4));
                Assert.Equal("https://article.example/story", reader.GetString(5));
                Assert.Equal(extractionId, reader.GetString(6));
                Assert.Equal(dateStart, reader.GetInt32(7));
                Assert.Equal(6, reader.GetInt32(8));
                Assert.False(await reader.ReadAsync());
            }
            await using (var quotations = new NpgsqlCommand("""
                SELECT revision_number, position, extraction_id, quotation_start, quotation_length, kind
                  FROM public_api.statement_quotations WHERE record_id = @id ORDER BY position
                """, connection))
            {
                quotations.Parameters.AddWithValue("id", active.RecordId);
                await using var reader = await quotations.ExecuteReaderAsync();
                Assert.True(await reader.ReadAsync());
                Assert.Equal(1, reader.GetInt32(0));
                Assert.Equal(0, reader.GetInt16(1));
                Assert.Equal(extractionId, reader.GetString(2));
                Assert.Equal(quoteStart, reader.GetInt32(3));
                Assert.Equal(9, reader.GetInt32(4));
                Assert.Equal("DirectQuotation", reader.GetString(5));
                Assert.False(await reader.ReadAsync());
            }
            foreach (var recordId in new[] { deactivated.RecordId, missingMetadata.RecordId, missingSpeaker.RecordId })
            {
                await using var absent = new NpgsqlCommand("""
                    SELECT (SELECT count(*) FROM public_api.statement_sources WHERE record_id = @id)
                         + (SELECT count(*) FROM public_api.statement_quotations WHERE record_id = @id)
                    """, connection);
                absent.Parameters.AddWithValue("id", recordId);
                Assert.Equal(0L, await absent.ExecuteScalarAsync());
            }
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task DowngradeDropsTheViewsAndMigrationRestoresThemAndTheirMissingColumnGrants()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);
            var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, "20261107170000_PublicApiStatementLists") < 0);
            await db.GetService<IMigrator>().MigrateAsync(previous);
            await using var connection = new NpgsqlConnection(database);
            await connection.OpenAsync();
            await using (var absent = new NpgsqlCommand("SELECT to_regclass('public_api.statements') IS NULL AND to_regclass('public_api.statement_speakers') IS NULL AND to_regclass('public_api.statement_sources') IS NULL AND to_regclass('public_api.statement_quotations') IS NULL", connection))
                Assert.Equal(true, await absent.ExecuteScalarAsync());

            var store = PostgresCollectionAttemptStore.FromConnectionString(database);
            await store.MigrateAsync();
            await ReadViewsAsync(connection);
            await using (var revoke = new NpgsqlCommand("""
                REVOKE SELECT (statement_date, summary) ON statement_revisions FROM civic_lens_public_owner;
                REVOKE SELECT (candidate_id, revision_number, official_id, "position") ON statement_revision_speakers FROM civic_lens_public_owner;
                REVOKE SELECT (candidate_id, revision_number, issue_id, "position") ON statement_revision_issues FROM civic_lens_public_owner;
                """, connection)) await revoke.ExecuteNonQueryAsync();
            var denied = await Assert.ThrowsAsync<PostgresException>(() => ReadViewsAsync(connection));
            Assert.Equal("42501", denied.SqlState);

            await store.MigrateAsync();
            await ReadViewsAsync(connection);
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task PublicRoleReadsOnlyTheListedStatementViewColumns()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var connection = new NpgsqlConnection(database);
            await connection.OpenAsync();
            await using (var read = new NpgsqlCommand("""
                SET ROLE civic_lens_public;
                SELECT record_id, revision_number, statement_date, headline, summary, text_origin, label, issue_ids
                  FROM public_api.statements LIMIT 0;
                SELECT record_id, revision_number, "position", official_id FROM public_api.statement_speakers LIMIT 0;
                SELECT record_id, revision_number, extraction_id, url, observed_at_utc_ticks, text, article_url,
                       statement_date_evidence_extraction_id, statement_date_evidence_start, statement_date_evidence_length
                  FROM public_api.statement_sources LIMIT 0;
                SELECT record_id, revision_number, position, extraction_id, quotation_start, quotation_length, kind
                  FROM public_api.statement_quotations LIMIT 0;
                """, connection))
                await read.ExecuteNonQueryAsync();
            foreach (var sql in new[]
            {
                "SELECT candidate_id FROM public.statement_revisions LIMIT 0",
                "SELECT extraction_id FROM public.document_extractions LIMIT 0",
                "SELECT position FROM public.statement_revision_quotations LIMIT 0",
                "SELECT requested_url FROM public.collection_attempts LIMIT 0"
            })
            {
                var exception = await Assert.ThrowsAsync<PostgresException>(async () =>
                {
                    await using var denied = new NpgsqlCommand(sql, connection);
                    await denied.ExecuteNonQueryAsync();
                });
                Assert.Equal("42501", exception.SqlState);
            }
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    private static async Task ReadViewsAsync(NpgsqlConnection connection)
    {
        try
        {
            await using var read = new NpgsqlCommand("""
                SET ROLE civic_lens_public;
                SELECT record_id, summary, statement_date, issue_ids FROM public_api.statements LIMIT 0;
                SELECT record_id, official_id, "position" FROM public_api.statement_speakers LIMIT 0;
                SELECT record_id, extraction_id, url, observed_at_utc_ticks, text, article_url FROM public_api.statement_sources LIMIT 0;
                SELECT record_id, extraction_id, position, quotation_start, quotation_length, kind FROM public_api.statement_quotations LIMIT 0;
                """, connection);
            await read.ExecuteNonQueryAsync();
        }
        finally
        {
            await using var reset = new NpgsqlCommand("RESET ROLE", connection);
            await reset.ExecuteNonQueryAsync();
        }
    }
}
