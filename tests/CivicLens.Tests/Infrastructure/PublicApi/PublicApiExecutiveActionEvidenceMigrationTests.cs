using CivicLens.Infrastructure.Collection.Migrations;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using static CivicLens.Tests.PublicApi.Votes.ViewsSeed;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiExecutiveActionEvidenceMigrationTests(PostgresCollection postgres)
{
    [Fact]
    public async Task DowngradeDropsTheEvidenceViewsTheQuoteColumnsAndTheOwnerGrantsAndUpgradeWithGrantsRestoresThem()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            var migration = new PublicApiExecutiveActionEvidence();
            Assert.Equal("true|true|true|11", await StateAsync(database));

            await ApplyAsync(database, migration.DownOperations);
            Assert.Equal("false|false|false|0", await StateAsync(database));
            Assert.Equal(["record_id,revision_number,position,sentence"], await SentenceColumnsAsync(database));
            Assert.Equal(["0"], await QueryAsPublicAsync(database, "SELECT count(*)::text FROM public_api.executive_action_summary_sentences"));

            await ApplyAsync(database, migration.UpOperations);
            await ApplyGrantsAsync(database);
            Assert.Equal("true|true|true|11", await StateAsync(database));
            Assert.Equal(["record_id,revision_number,position,sentence,extraction_id,quote_start,quote_length"], await SentenceColumnsAsync(database));
            Assert.Equal(["0|0"], await QueryAsPublicAsync(database,
                "SELECT (SELECT count(*) FROM public_api.executive_action_facts)::text || '|' || (SELECT count(*) FROM public_api.executive_action_summary_sources)::text"));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    // Each new view is a security barrier view that the role reads. The owner holds the nine added base columns, and the sentence view has two quote columns: 11.
    private static async Task<string> StateAsync(string database) => (await QueryAsync(database, """
        SELECT (to_regclass('public_api.executive_action_facts') IS NOT NULL
                AND has_table_privilege('civic_lens_public', to_regclass('public_api.executive_action_facts'), 'SELECT'))::text || '|'
            || (to_regclass('public_api.executive_action_summary_sources') IS NOT NULL
                AND has_table_privilege('civic_lens_public', to_regclass('public_api.executive_action_summary_sources'), 'SELECT'))::text || '|'
            || coalesce((SELECT bool_and(reloptions @> ARRAY['security_barrier=true']) FROM pg_class
                 WHERE oid IN (to_regclass('public_api.executive_action_facts'), to_regclass('public_api.executive_action_summary_sources'))), false)::text || '|'
            || ((SELECT count(*) FROM (VALUES
                ('executive_action_revision_facts', 'citation_start'), ('executive_action_revision_facts', 'citation_length'),
                ('executive_action_revisions', 'disposition_notes_start'), ('executive_action_revisions', 'disposition_notes_length'),
                ('executive_action_documents', 'notice_citation_start'), ('executive_action_documents', 'notice_citation_length'),
                ('executive_action_revision_summary_sentences', 'quote_extraction_id'), ('executive_action_revision_summary_sentences', 'quote_start'),
                ('executive_action_revision_summary_sentences', 'quote_length')) added(table_name, column_name)
                WHERE has_column_privilege('civic_lens_public_owner', added.table_name, added.column_name, 'SELECT'))
               + (SELECT count(*) FROM information_schema.columns
                   WHERE table_schema = 'public_api' AND table_name = 'executive_action_summary_sentences'
                     AND column_name IN ('extraction_id', 'quote_start')))::text
        """))[0];

    private static Task<string[]> SentenceColumnsAsync(string database) => QueryAsync(database, """
        SELECT string_agg(column_name, ',' ORDER BY ordinal_position) FROM information_schema.columns
         WHERE table_schema = 'public_api' AND table_name = 'executive_action_summary_sentences'
        """);

    private static async Task ApplyAsync(string database, IReadOnlyList<MigrationOperation> operations)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var transaction = await connection.BeginTransactionAsync();
        foreach (var operation in operations)
        {
            await using var command = new NpgsqlCommand(Assert.IsType<SqlOperation>(operation).Sql, connection, transaction);
            await command.ExecuteNonQueryAsync();
        }
        await transaction.CommitAsync();
    }
}
