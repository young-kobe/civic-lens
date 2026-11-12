using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Officials;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiBillSponsorPartiesAndTimesMigrationTests(PostgresCollection postgres)
{
    private const string PreviousMigration = "20261112040000_DropPublicApiOfficialBillCounts";

    [Fact]
    public async Task DowngradeRestoresTheBillViewsAndUpgradeGivesThePublicRoleThePartyAndTheTimes()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            var record = new string('a', 64);
            await ViewsSeed.ActivateReleaseAsync(database, 1, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
            await BillsSeed.AddBillsAsync(database, new SeededBill('a', "2026-03-02") { Sponsorships = [new("p1", "Sponsor", "2026-03-02")] });
            await OfficialsSeed.AddOfficialsAsync(database,
                new SeededOfficial("p1", "Pat One", new SeededTerm("House", "Representative", "2020-01-01") { Party = "Democrat", State = "MA", District = 1 }));
            await ViewsSeed.AddVotesAsync(database, new SeededVote('e', "2026-03-03") { BillNumber = 7 });
            await ViewsSeed.ExecuteAsync(database, $"""
                SET session_replication_role = replica;
                INSERT INTO action_review_decisions (decision_id, record_id, revision_number, review_state_version, kind, actor_subject, created_at_utc_ticks)
                VALUES ('{new string('1', 32)}', '{record}', 1, 1, 'Approve', 'code', 5);
                INSERT INTO published_records (kind, record_id, first_published_at_utc_ticks) VALUES ('action', '{record}', 7);
                INSERT INTO release_vote_bills (release_number, vote_record_id, bill_record_id) VALUES (1, '{new string('e', 64)}', '{record}');
                RESET session_replication_role;
                """);
            await using (var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options))
                await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            await using (var connection = new NpgsqlConnection(database))
            {
                await connection.OpenAsync();
                await using var missing = new NpgsqlCommand("""
                    SELECT to_regclass('public_api.bill_sponsorship_parties') IS NULL
                       AND NOT EXISTS (SELECT 1 FROM information_schema.columns WHERE table_schema = 'public_api' AND table_name = 'bills' AND column_name = 'checked_at_utc_ticks')
                       AND (SELECT count(*) FROM public_api.votes WHERE bill_record_id IS NOT NULL) = 1
                       AND (SELECT count(*) FROM public_api.bill_actions) = 0
                    """, connection);
                Assert.True((bool)(await missing.ExecuteScalarAsync())!);
            }

            await PostgresCollectionAttemptStore.FromConnectionString(database).MigrateAsync();
            await using var publicRead = new NpgsqlConnection(database);
            await publicRead.OpenAsync();
            await using (var role = new NpgsqlCommand("SET ROLE civic_lens_public", publicRead)) await role.ExecuteNonQueryAsync();
            await using var view = new NpgsqlCommand("""
                SELECT p.party || ' ' || b.checked_at_utc_ticks || ' ' || b.first_published_at_utc_ticks || ' ' || b.summary_from_crs
                  FROM public_api.bills b JOIN public_api.bill_sponsorship_parties p ON p.record_id = b.record_id AND p.revision_number = b.revision_number
                """, publicRead);
            Assert.Equal("Democrat 5 7 false", await view.ExecuteScalarAsync());
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }
}
