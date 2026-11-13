using CivicLens.Infrastructure.Collection.Migrations;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using static CivicLens.Tests.PublicApi.Votes.ViewsSeed;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiRelationshipsMigrationTests(PostgresCollection postgres)
{
    [Fact]
    public async Task DowngradeDropsTheRelationshipViewAndItsOwnerGrantsAndUpgradeWithGrantsRestoresThem()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            var migration = new PublicApiRelationships();
            Assert.Equal("true|true|8", await StateAsync(database));

            await ApplyAsync(database, migration.DownOperations);
            Assert.Equal("false|false|0", await StateAsync(database));
            // The record links view still reads the reason and the quote span, so the owner keeps them.
            Assert.Equal(["0"], await QueryAsPublicAsync(database, "SELECT count(*)::text FROM public_api.record_links"));

            await ApplyAsync(database, migration.UpOperations);
            await ApplyGrantsAsync(database);
            Assert.Equal("true|true|8", await StateAsync(database));
            Assert.Equal(["0"], await QueryAsPublicAsync(database, "SELECT count(*)::text FROM public_api.relationships"));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    // The view is a security barrier view that the role reads, and the owner holds the eight added relationship revision columns.
    private static async Task<string> StateAsync(string database) => (await QueryAsync(database, """
        SELECT (to_regclass('public_api.relationships') IS NOT NULL
                AND has_table_privilege('civic_lens_public', to_regclass('public_api.relationships'), 'SELECT'))::text || '|'
            || coalesce((SELECT reloptions @> ARRAY['security_barrier=true'] FROM pg_class WHERE oid = to_regclass('public_api.relationships')), false)::text || '|'
            || (SELECT count(*) FROM unnest(ARRAY['author_kind', 'statement_revision_number', 'target_kind', 'target_action_record_id',
                    'target_action_revision_number', 'target_vote_record_id', 'target_vote_revision_number', 'target_fact_id']) added(column_name)
                WHERE has_column_privilege('civic_lens_public_owner', 'relationship_revisions', added.column_name, 'SELECT'))::text
        """))[0];

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
