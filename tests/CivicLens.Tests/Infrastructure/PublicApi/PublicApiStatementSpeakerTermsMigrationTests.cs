using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Officials;
using CivicLens.Tests.PublicApi.Statements;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiStatementSpeakerTermsMigrationTests(PostgresCollection postgres)
{
    private const string PreviousMigration = "20261112150000_PublicApiBillSponsorPartiesAndTimes";

    [Fact]
    public async Task DowngradeKeepsTheBillPartiesAndUpgradeGivesThePublicRoleTheSpeakerPartyAndSeatOfTheStatementDate()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await ViewsSeed.ActivateReleaseAsync(database, 1, new DateTimeOffset(2026, 6, 1, 0, 0, 0, TimeSpan.Zero));
            await BillsSeed.AddBillsAsync(database, new SeededBill('a', "2026-03-02") { Sponsorships = [new("p1", "Sponsor", "2026-03-02")] });
            await StatementsSeed.AddAsync(database, new SeededStatement('b', "2026-03-02", ["p1"], [], "Analysis", "Code"));
            await OfficialsSeed.AddOfficialsAsync(database, new SeededOfficial("p1", "Pat One",
                new SeededTerm("House", "Representative", "2020-01-01") { Party = "Democrat", State = "MA", District = 1, EndsOn = "2026-04-30" },
                new SeededTerm("Senate", "Senator", "2026-05-01") { Party = "Independent", State = "MA" }));
            await using (var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options))
                await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
            Assert.Equal("missing Democrat", await ScalarAsync(database, """
                SELECT CASE WHEN to_regclass('public_api.statement_speaker_terms') IS NULL THEN 'missing' ELSE 'present' END || ' ' || p.party
                  FROM public_api.bill_sponsorship_parties p
                """));

            await PostgresCollectionAttemptStore.FromConnectionString(database).MigrateAsync();

            Assert.Equal("Democrat Democrat Representative House MA 1", await ScalarAsync(database, """
                SELECT b.party || ' ' || t.party || ' ' || t.kind || ' ' || t.office || ' ' || t.state || ' ' || t.district
                  FROM public_api.bill_sponsorship_parties b CROSS JOIN public_api.statement_speaker_terms t
                """, asPublicRole: true));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    private static async Task<object?> ScalarAsync(string database, string sql, bool asPublicRole = false)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        if (asPublicRole)
        {
            await using var role = new NpgsqlCommand("SET ROLE civic_lens_public", connection);
            await role.ExecuteNonQueryAsync();
        }
        await using var command = new NpgsqlCommand(sql, connection);
        return await command.ExecuteScalarAsync();
    }
}
