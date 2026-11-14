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
public sealed class PublicApiBillKeysMigrationTests(PostgresCollection postgres)
{
    private const string KeysSql = """
        SELECT concat_ws('|', left(record_id, 1), revision_number, congress, bill_type, bill_number, coalesce(policy_area_id, '-'), event_date, coalesce(status, '-'))
          FROM public_api.bill_keys ORDER BY record_id
        """;

    // The keys hold the bills and the stage of bill_status, with a null stage where bill_status holds no row or no stage.
    private const string BillsWithStatusSql = """
        SELECT concat_ws('|', left(b.record_id, 1), b.revision_number, b.congress, b.bill_type, b.bill_number, coalesce(b.policy_area_id, '-'), b.event_date, coalesce(t.status, '-'))
          FROM public_api.bills b
          LEFT JOIN public_api.bill_status t ON t.record_id = b.record_id AND t.revision_number = b.revision_number
         ORDER BY b.record_id
        """;

    // Bill a has a stage in the active release and another stage in an inactive release. Bill b has a row with no stage. Bill c has no row.
    // Bill d has no sponsorship and bill e enters only in a later release, so bills hides both and so must the keys.
    private static readonly string[] ExpectedKeys =
    [
        "a|1|119|hr|4|health|2026-03-01|InCommittee",
        "b|1|118|s|5|-|2025-03-02|-",
        "c|1|119|hr|6|tax|2026-03-03|-"
    ];

    [Fact]
    public async Task TheKeysHoldEveryBillOfTheBillsViewWithItsActiveStageAndTheRollbackDropsOnlyTheView()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            var published = new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero);
            await ViewsSeed.ActivateReleaseAsync(database, 1, published);
            await BillsSeed.AddBillsAsync(database,
                new SeededBill('a', "2026-03-01") { BillNumber = 4 },
                new SeededBill('b', "2025-03-02") { Congress = 118, BillType = "s", BillNumber = 5, PolicyAreaId = null },
                new SeededBill('c', "2026-03-03") { BillNumber = 6, PolicyAreaId = "tax" },
                new SeededBill('d', "2026-03-04") { BillNumber = 7, Sponsorships = [] },
                new SeededBill('e', "2026-03-05") { BillNumber = 8, FirstRelease = 2 });
            await ViewsSeed.ExecuteAsync(database, "SET session_replication_role = replica;\n"
                + PublicRecordPageRows.PinnedStatus(1, new string('a', 64), "Referred.", "2026-03-02", published.UtcTicks, "InCommittee") + "\n"
                + PublicRecordPageRows.PinnedStatus(2, new string('a', 64), "Passed.", "2026-03-02", published.UtcTicks, "PassedHouse") + "\n"
                + PublicRecordPageRows.PinnedStatus(1, new string('b', 64), "Introduced.", "2025-03-02", published.UtcTicks) + "\n"
                + PublicRecordPageRows.PinnedStatus(1, new string('d', 64), "Referred.", "2026-03-04", published.UtcTicks, "InCommittee"));

            Assert.Equal(ExpectedKeys, await QueryAsPublicAsync(database, KeysSql));
            Assert.Equal(ExpectedKeys, await QueryAsPublicAsync(database, BillsWithStatusSql));

            var migration = new PublicApiBillKeys();
            await ApplyAsync(database, migration.DownOperations);
            Assert.Empty(await QueryAsync(database, "SELECT 1 FROM pg_class WHERE oid = to_regclass('public_api.bill_keys')"));
            Assert.Equal(ExpectedKeys, await QueryAsPublicAsync(database, BillsWithStatusSql));

            await ApplyAsync(database, migration.UpOperations);
            await ViewsSeed.ExecuteAsync(database, "GRANT SELECT ON public_api.bill_keys TO civic_lens_public");
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
