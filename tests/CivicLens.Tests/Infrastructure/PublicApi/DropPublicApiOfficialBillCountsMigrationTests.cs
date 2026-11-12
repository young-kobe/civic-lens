using CivicLens.Infrastructure.Collection.Migrations;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class DropPublicApiOfficialBillCountsMigrationTests(PostgresCollection postgres)
{
    private const string ViewStateSql = """
        SELECT coalesce((SELECT string_agg(attname, ',' ORDER BY attnum) FROM pg_attribute
                          WHERE attrelid = to_regclass('public_api.official_bill_counts') AND attnum > 0 AND NOT attisdropped), 'none')
            || '|' || coalesce((SELECT pg_get_userbyid(relowner) FROM pg_class WHERE oid = to_regclass('public_api.official_bill_counts')), 'none')
            || '|' || CASE WHEN to_regclass('public_api.official_bill_counts') IS NULL THEN 'none'
                           ELSE has_table_privilege('civic_lens_public', 'public_api.official_bill_counts', 'SELECT')::text END
        """;

    [Fact]
    public async Task TheViewIsGoneAfterMigrateAndDowngradeRestoresItsColumnsOwnerAndPublicGrantAndUpgradeDropsItAgain()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            Assert.Equal("none|none|none", await ScalarAsync(database));
            var migration = new DropPublicApiOfficialBillCounts();

            await ApplyAsync(database, migration.DownOperations);
            Assert.Equal("official_id,congress,sponsored,cosponsored|civic_lens_public_owner|true", await ScalarAsync(database));
            Assert.Equal("{security_barrier=true}", await ScalarAsync(database, "SELECT reloptions::text FROM pg_class WHERE oid = 'public_api.official_bill_counts'::regclass"));
            var definition = await ScalarAsync(database, "SELECT pg_get_viewdef('public_api.official_bill_counts'::regclass)");
            Assert.Contains("withdrawn_on IS NULL", definition);
            Assert.Contains("'Sponsor'::text", definition);
            Assert.Contains("active_records", definition);

            await ApplyAsync(database, migration.UpOperations);
            Assert.Equal("none|none|none", await ScalarAsync(database));
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

    private static async Task<string> ScalarAsync(string database, string sql = ViewStateSql)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        return (string)(await command.ExecuteScalarAsync())!;
    }
}
