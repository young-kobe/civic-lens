using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Collection.Migrations;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations.Operations;
using Npgsql;
using static CivicLens.Tests.PublicApi.Votes.ViewsSeed;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class MemberVoteCompletenessMigrationTests(PostgresCollection postgres)
{
    [Fact]
    public async Task AppendedMarkerDistinguishesKnownNullFromUnwrittenAndSurvivesDowngradeWithoutWiderGrants()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await ApplyAsync(database, new PublicApiOfficialVoteParties().DownOperations);
            await ApplyAsync(database, new PublicApiMemberVoteTextOrigin().DownOperations);
            await ApplyAsync(database, new PublicApiMemberVoteBreaksParty().DownOperations);
            var migration = new PublicApiMemberVoteCompleteness();
            await UpgradeAsync(database, migration);
            var columns = await ColumnsAsync(database);
            Assert.Equal($"alignment_known|boolean|{columns.Length}", columns[^1]);

            await ViewsSeed.ActivateReleaseAsync(database, 1, DateTimeOffset.UnixEpoch, recordCount: 3);
            await ViewsSeed.AddVotesAsync(database,
                new SeededVote('a', "2025-03-01") { ActiveRevision = 2, Casts = ["Yea"], PersonIds = ["member-a"] },
                new SeededVote('b', "2025-03-02") { Casts = ["Nay"], PersonIds = ["member-b"] },
                new SeededVote('c', "2025-03-03") { Casts = ["Yea"], PersonIds = ["member-c"] });
            await ViewsSeed.ExecuteAsync(database, """
                SET session_replication_role = replica;
                UPDATE vote_revisions
                   SET columns_written = true,
                       kind = CASE WHEN left(record_id, 1) = 'a' AND revision_number = 2 THEN 'Rule' ELSE 'Passage' END,
                       alignment = CASE WHEN left(record_id, 1) = 'a' AND revision_number = 2 THEN NULL ELSE 'Split' END,
                       democrat_majority = 'Yea', republican_majority = 'Nay'
                 WHERE left(record_id, 1) IN ('a', 'c');
                UPDATE vote_member_list_entries SET party = 'Democrat'
                 WHERE person_id IN ('member-a', 'member-c');
                """);

            await AssertMarkerValuesAsync(database);
            var partyLines = await QueryAsPublicAsync(database,
                "SELECT official_id || '|' || cast_count FROM public_api.official_party_lines ORDER BY official_id");
            Assert.Equal(["member-c|1"], partyLines);
            await AssertBaseTableDeniedAsync(database);

            await ApplyAsync(database, migration.DownOperations);
            Assert.Equal(columns[..^1], await ColumnsAsync(database));
            Assert.Equal(partyLines, await QueryAsPublicAsync(database,
                "SELECT official_id || '|' || cast_count FROM public_api.official_party_lines ORDER BY official_id"));
            Assert.Equal(["member-a|Democrat", "member-b|", "member-c|Democrat"], await QueryAsPublicAsync(database,
                "SELECT official_id || '|' || coalesce(party, '') FROM public_api.member_votes ORDER BY official_id"));

            await UpgradeAsync(database, migration);
            Assert.Equal(columns, await ColumnsAsync(database));
            await AssertMarkerValuesAsync(database);
            Assert.Equal(partyLines, await QueryAsPublicAsync(database,
                "SELECT official_id || '|' || cast_count FROM public_api.official_party_lines ORDER BY official_id"));
            await AssertBaseTableDeniedAsync(database);
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    private static async Task AssertMarkerValuesAsync(string database) =>
        Assert.Equal(["member-a|true||Democrat", "member-b|false||", "member-c|true|Split|Democrat"],
            await QueryAsPublicAsync(database,
                "SELECT official_id || '|' || alignment_known::text || '|' || coalesce(alignment, '') || '|' || coalesce(party, '') FROM public_api.member_votes ORDER BY official_id"));

    // The grants of db migrate name views of later migrations, which the test rolled back. So the test grants the one column that db migrate gives the owner for this view.
    private static async Task UpgradeAsync(string database, PublicApiMemberVoteCompleteness migration)
    {
        await ApplyAsync(database, migration.UpOperations);
        await ExecuteAsync(database, "GRANT SELECT (columns_written) ON public.vote_revisions TO civic_lens_public_owner");
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

    private static Task<string[]> ColumnsAsync(string database) =>
        QueryAsync(database, "SELECT column_name || '|' || data_type || '|' || ordinal_position FROM information_schema.columns WHERE table_schema = 'public_api' AND table_name = 'member_votes' ORDER BY ordinal_position");

    private static async Task AssertBaseTableDeniedAsync(string database)
    {
        var exception = await Assert.ThrowsAsync<PostgresException>(() => QueryAsPublicAsync(database, "SELECT columns_written::text FROM public.vote_revisions"));
        Assert.Equal(PostgresErrorCodes.InsufficientPrivilege, exception.SqlState);
    }
}
