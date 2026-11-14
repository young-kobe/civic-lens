using CivicLens.Infrastructure.Collection.Migrations;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Relationships;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using static CivicLens.Tests.PublicApi.Votes.ViewsSeed;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiRelationshipFactTextOriginMigrationTests(PostgresCollection postgres)
{
    private const string OriginsSql =
        "SELECT record_id || '|' || coalesce(fact_label, '-') || '|' || coalesce(fact_text_origin, '-') FROM public_api.relationships ORDER BY record_id";

    private const string FactsSql = "SELECT record_id || '|' || fact || '|' || coalesce(fact_label, '-') FROM public_api.relationships ORDER BY record_id";

    [Fact]
    public async Task TheLastColumnNamesWhoWroteTheFactAndTheRollbackDropsOnlyThatColumnAndKeepsTheGrant()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await ViewsSeed.ActivateReleaseAsync(database, 1, new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero));
            await RelationshipSeed.SeedAsync(database);
            await ViewsSeed.ExecuteAsync(database,
                $"SET session_replication_role = replica; UPDATE action_revisions SET text_origin = 'Code' WHERE record_id = '{RelationshipSeed.ActionId}';");
            string[] expectedOrigins =
                [$"{RelationshipSeed.BillLink}|{AiLabelWords.CodeWording}|Code", $"{RelationshipSeed.VoteLink}|-|-"];

            Assert.Equal(expectedOrigins, await QueryAsPublicAsync(database, OriginsSql));
            var facts = await QueryAsPublicAsync(database, FactsSql);

            var migration = new PublicApiRelationshipFactTextOrigin();
            var columns = await ColumnNamesAsync(database);
            Assert.Equal("fact_text_origin", columns[^1]);
            await ApplyAsync(database, migration.DownOperations);
            Assert.Equal(columns[..^1], await ColumnNamesAsync(database));
            Assert.Equal(facts, await QueryAsPublicAsync(database, FactsSql));
            await ApplyAsync(database, migration.UpOperations);
            Assert.Equal(columns, await ColumnNamesAsync(database));
            Assert.Equal(expectedOrigins, await QueryAsPublicAsync(database, OriginsSql));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

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

    private static Task<string[]> ColumnNamesAsync(string database) => QueryAsync(database,
        "SELECT column_name FROM information_schema.columns WHERE table_schema = 'public_api' AND table_name = 'relationships' ORDER BY ordinal_position");
}
