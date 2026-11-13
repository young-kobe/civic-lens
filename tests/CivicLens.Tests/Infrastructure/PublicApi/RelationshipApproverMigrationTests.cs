using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class RelationshipApproverMigrationTests(PostgresCollection postgres)
{
    private const string Migration = "20261113020000_RelationshipApprover";

    [Fact]
    public async Task DowngradeClearsTheApproversOfRelationshipEntriesBeforeItRestoresTheStatementOnlyConstraint()
    {
        var connectionString = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
            var migrator = db.GetService<IMigrator>();
            await migrator.MigrateAsync(Migration);
            await using var connection = new NpgsqlConnection(connectionString);
            await connection.OpenAsync();
            var link = new string('7', 64);
            await using (var seed = new NpgsqlCommand($"""
                SET session_replication_role = replica;
                INSERT INTO published_entries (kind, record_id, revision_number, relationship_record_id, relationship_revision_number, headline, link_type,
                    statement_record_id, target_kind, target_record_id, approved_by)
                  VALUES ('relationship', '{link}', 1, '{link}', 1, 'Link', 'DescribesOwnBill', '{new string('1', 64)}', 'action', '{new string('a', 64)}', 'Code');
                """, connection))
                await seed.ExecuteNonQueryAsync();

            var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, Migration) < 0);
            await migrator.MigrateAsync(previous);

            await using var read = new NpgsqlCommand($"SELECT approved_by IS NULL FROM published_entries WHERE record_id = '{link}'", connection);
            Assert.True((bool)(await read.ExecuteScalarAsync())!);
        }
        finally { await postgres.DropDatabaseAsync(connectionString); }
    }
}
