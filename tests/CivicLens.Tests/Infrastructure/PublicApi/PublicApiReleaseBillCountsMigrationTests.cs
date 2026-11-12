using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Migrations;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using static CivicLens.Tests.PublicApi.Votes.ViewsSeed;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiReleaseBillCountsMigrationTests(PostgresCollection postgres)
{
    private const string GrantStateSql = """
        SELECT has_any_column_privilege('civic_lens_public_owner', 'release_bill_counts', 'SELECT')::text
            || '|' || has_any_column_privilege('civic_lens_public_owner', 'release_official_bill_counts', 'SELECT')::text
            || '|' || (to_regclass('public_api.release_bill_counts') IS NULL)::text
            || '|' || (to_regclass('public_api.release_official_bill_counts') IS NULL)::text
        """;

    [Fact]
    public async Task DowngradeDropsTheViewsAndRevokesTheOwnerColumnGrantsAndUpgradeWithGrantsRestoresThem()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            Assert.Equal("true|true|false|false", await ScalarAsync(database));
            var migration = new PublicApiReleaseBillCounts();

            await ApplyAsync(database, migration.DownOperations);
            Assert.Equal("false|false|true|true", await ScalarAsync(database));

            await ApplyAsync(database, migration.UpOperations);
            await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);
            await ApplyGrantsAsync(database);
            Assert.Equal("true|true|false|false", await ScalarAsync(database));
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

    private static async Task<string> ScalarAsync(string database)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(GrantStateSql, connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
