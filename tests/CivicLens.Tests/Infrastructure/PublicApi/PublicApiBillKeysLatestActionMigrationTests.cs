using CivicLens.Infrastructure.Collection.Migrations;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using static CivicLens.Tests.PublicApi.Votes.ViewsSeed;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiBillKeysLatestActionMigrationTests(PostgresCollection postgres)
{
    private const string KeysSql = """
        SELECT concat_ws('|', left(record_id, 1), coalesce(status, '-'), coalesce(latest_action_date::text, '-')) FROM public_api.bill_keys ORDER BY record_id
        """;

    // The keys hold the latest action date that bill_status shows for the bill, with a null date where bill_status holds no row.
    private const string BillsWithStatusSql = """
        SELECT concat_ws('|', left(b.record_id, 1), coalesce(t.status, '-'), coalesce(t.latest_action_date::text, '-'))
          FROM public_api.bills b
          LEFT JOIN public_api.bill_status t ON t.record_id = b.record_id AND t.revision_number = b.revision_number
         ORDER BY b.record_id
        """;

    private const string ColumnsSql = """
        SELECT attname FROM pg_attribute WHERE attrelid = to_regclass('public_api.bill_keys') AND attnum > 0 AND NOT attisdropped ORDER BY attnum
        """;

    private static readonly string[] EarlierColumns = ["record_id", "revision_number", "congress", "bill_type", "bill_number", "policy_area_id", "event_date", "status"];
    private static readonly string[] Columns = [.. EarlierColumns, "latest_action_date"];

    // Bill a has a newer action in an inactive release, so the keys must take the date of the active release. Bill b has a row with no stage. Bill c has no row.
    private static readonly string[] ExpectedKeys = ["a|InCommittee|2026-03-02", "b|-|2025-04-09", "c|-|-"];

    [Fact]
    public async Task TheKeysHoldTheLatestActionDateOfTheActiveReleaseAndTheRollbackRestoresTheEarlierViewWithItsGrant()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            var published = new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero);
            await ViewsSeed.ActivateReleaseAsync(database, 1, published);
            await BillsSeed.AddBillsAsync(database,
                new SeededBill('a', "2026-03-01") { BillNumber = 4 },
                new SeededBill('b', "2025-03-02") { Congress = 118, BillType = "s", BillNumber = 5 },
                new SeededBill('c', "2026-03-03") { BillNumber = 6 });
            await ViewsSeed.ExecuteAsync(database, "SET session_replication_role = replica;\n"
                + PublicRecordPageRows.PinnedStatus(1, new string('a', 64), "Referred.", "2026-03-02", published.UtcTicks, "InCommittee") + "\n"
                + PublicRecordPageRows.PinnedStatus(2, new string('a', 64), "Passed.", "2026-05-02", published.UtcTicks, "PassedHouse") + "\n"
                + PublicRecordPageRows.PinnedStatus(1, new string('b', 64), "Introduced.", "2025-04-09", published.UtcTicks));

            Assert.Equal(ExpectedKeys, await QueryAsPublicAsync(database, KeysSql));
            Assert.Equal(ExpectedKeys, await QueryAsPublicAsync(database, BillsWithStatusSql));
            Assert.Equal(Columns, await QueryAsync(database, ColumnsSql));

            var migration = new PublicApiBillKeysLatestAction();
            await ApplyAsync(database, migration.DownOperations);
            Assert.Equal(EarlierColumns, await QueryAsync(database, ColumnsSql));
            Assert.Equal(["a|InCommittee", "b|-", "c|-"],
                await QueryAsPublicAsync(database, "SELECT concat_ws('|', left(record_id, 1), coalesce(status, '-')) FROM public_api.bill_keys ORDER BY record_id"));

            await ApplyAsync(database, migration.UpOperations);
            Assert.Equal(ExpectedKeys, await QueryAsPublicAsync(database, KeysSql));
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
}
