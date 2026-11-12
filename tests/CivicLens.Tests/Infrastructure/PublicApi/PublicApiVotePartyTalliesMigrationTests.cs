using CivicLens.Infrastructure.Collection.Migrations;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using static CivicLens.Tests.PublicApi.Votes.ViewsSeed;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PublicApiVotePartyTalliesMigrationTests(PostgresCollection postgres)
{
    [Fact]
    public async Task PublishedOnlyViewIsGrantedToThePublicRoleWithoutGrantingBaseTables()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await ViewsSeed.ActivateReleaseAsync(database, 1, DateTimeOffset.UnixEpoch);
            await ViewsSeed.ActivateReleaseAsync(database, 2, DateTimeOffset.UnixEpoch.AddDays(1));
            var active = new SeededVote('a', "2026-01-01") { ActiveRevision = 2 };
            var retired = new SeededVote('b', "2026-01-02") { LastRelease = 1 };
            await ViewsSeed.AddVotesAsync(database, active, retired);
            await ViewsSeed.ExecuteAsync(database, $"""
                UPDATE vote_revisions SET party_tally_count = 1 WHERE record_id = '{active.RecordId}' AND revision_number = 1;
                INSERT INTO vote_revision_party_tallies (record_id, revision_number, party, yea, nay, present, not_voting, other)
                VALUES ('{active.RecordId}', 1, 'Older', 1, 0, 0, 0, 0);
                UPDATE vote_revisions SET party_tally_count = 0 WHERE record_id = '{active.RecordId}' AND revision_number = 2;
                UPDATE vote_revisions SET party_tally_count = 1 WHERE record_id = '{retired.RecordId}' AND revision_number = 1;
                INSERT INTO vote_revision_party_tallies (record_id, revision_number, party, yea, nay, present, not_voting, other)
                VALUES ('{retired.RecordId}', 1, 'Retired', 1, 0, 0, 0, 0);
                """);

            await using var connection = new NpgsqlConnection(database);
            await connection.OpenAsync();
            await using (var role = new NpgsqlCommand("SET ROLE civic_lens_public", connection)) await role.ExecuteNonQueryAsync();
            await using (var rows = new NpgsqlCommand("SELECT record_id || '|' || revision_number || '|' || party_tally_count || '|' || coalesce(party, '') FROM public_api.vote_party_tallies ORDER BY record_id, revision_number, party", connection))
            await using (var reader = await rows.ExecuteReaderAsync())
            {
                Assert.True(await reader.ReadAsync());
                Assert.Equal($"{active.RecordId}|2|0|", reader.GetString(0));
                Assert.False(await reader.ReadAsync());
            }
            await using var privateTable = new NpgsqlCommand("SELECT count(*) FROM vote_revision_party_tallies", connection);
            var denied = await Assert.ThrowsAsync<PostgresException>(() => privateTable.ExecuteScalarAsync());
            Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, denied.SqlState);
            await connection.CloseAsync();

            var migration = new PublicApiVotePartyTallies();
            await ApplyAsync(database, migration.DownOperations);
            Assert.Equal("false|false|true", await GrantStateAsync(database));
            await ApplyAsync(database, migration.UpOperations);
            await ApplyGrantsAsync(database);
            Assert.Equal("true|true|false", await GrantStateAsync(database));
            await connection.OpenAsync();
            await using (var role = new NpgsqlCommand("SET ROLE civic_lens_public", connection)) await role.ExecuteNonQueryAsync();
            await using var restored = new NpgsqlCommand("SELECT count(*) FROM public_api.vote_party_tallies", connection);
            Assert.Equal(1L, await restored.ExecuteScalarAsync());
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    private static async Task<string> GrantStateAsync(string database)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT has_column_privilege('civic_lens_public_owner', 'vote_revisions', 'party_tally_count', 'SELECT')::text
                || '|' || has_any_column_privilege('civic_lens_public_owner', 'vote_revision_party_tallies', 'SELECT')::text
                || '|' || (to_regclass('public_api.vote_party_tallies') IS NULL)::text
            """, connection);
        return (string)(await command.ExecuteScalarAsync())!;
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
