using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiHomeIssueCongressMigrationTests(PostgresCollection postgres)
{
    private const string Migration = "20261107230000_PublicApiHomeIssueCongress";

    [Fact]
    public async Task UpgradeAppendsCongressAndRoleReadsItAndDowngradeRestoresTheOriginalViewColumns()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);
            var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, Migration) < 0);
            await db.GetService<IMigrator>().MigrateAsync(previous);
            var beforeVotes = await ColumnsAsync(database, "issue_votes");
            var beforeBills = await ColumnsAsync(database, "bill_issues");
            var store = PostgresCollectionAttemptStore.FromConnectionString(database);
            await store.MigrateAsync();

            var afterVotes = await ColumnsAsync(database, "issue_votes");
            var afterBills = await ColumnsAsync(database, "bill_issues");
            Assert.Equal(beforeVotes.Append($"congress|integer|{beforeVotes.Length + 1}"), afterVotes);
            Assert.Equal(beforeBills.Append($"congress|integer|{beforeBills.Length + 1}"), afterBills);
            await ReadCongressAsync(database);

            await using (var connection = new NpgsqlConnection(database))
            {
                await connection.OpenAsync();
                await using var read = new NpgsqlCommand("SET ROLE civic_lens_public; SELECT congress FROM public.action_revisions LIMIT 0", connection);
                var privateTable = await Assert.ThrowsAsync<PostgresException>(() => read.ExecuteNonQueryAsync());
                Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, privateTable.SqlState);
            }

            await using (var connection = new NpgsqlConnection(database))
            {
                await connection.OpenAsync();
                await using var revoke = new NpgsqlCommand("REVOKE SELECT ON public_api.issue_votes FROM civic_lens_public", connection);
                await revoke.ExecuteNonQueryAsync();
            }
            var denied = await Assert.ThrowsAsync<PostgresException>(() => ReadCongressAsync(database));
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
            await store.MigrateAsync();
            await ReadCongressAsync(database);

            await db.GetService<IMigrator>().MigrateAsync(previous);
            Assert.Equal(beforeVotes, await ColumnsAsync(database, "issue_votes"));
            Assert.Equal(beforeBills, await ColumnsAsync(database, "bill_issues"));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    private static async Task ReadCongressAsync(string database)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var read = new NpgsqlCommand("SET ROLE civic_lens_public; SELECT congress FROM public_api.issue_votes LIMIT 0; SELECT congress FROM public_api.bill_issues LIMIT 0;", connection);
        await read.ExecuteNonQueryAsync();
    }

    private static async Task<string[]> ColumnsAsync(string database, string view)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT column_name || '|' || data_type || '|' || ordinal_position FROM information_schema.columns WHERE table_schema = 'public_api' AND table_name = @view ORDER BY ordinal_position", connection);
        command.Parameters.AddWithValue("view", view);
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) columns.Add(reader.GetString(0));
        return [.. columns];
    }
}
