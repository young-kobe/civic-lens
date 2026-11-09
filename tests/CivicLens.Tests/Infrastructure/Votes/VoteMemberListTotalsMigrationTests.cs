using CivicLens.Infrastructure.Collection;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Votes;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class VoteMemberListTotalsMigrationTests(PostgresCollection postgres)
{
    private const string Migration = "20261109203000_VoteMemberListTotals";
    private static readonly string First = new('a', 64);
    private static readonly string Second = new('b', 64);

    [Fact]
    public async Task UpgradeCountsEveryRawCastAndRoundTripPreservesEntriesIdentityAndPublicColumnTypes()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var db = Context(database);
            var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, Migration) < 0);
            await db.GetService<IMigrator>().MigrateAsync(previous);
            await SeedListAsync(database, First, ["Yea", "Aye", "Nay", "No", "Present", "Not Voting", "Jeffries", "yea"]);
            await SeedListAsync(database, Second, ["Yes"]);
            var entries = await EntriesAsync(database);
            var columns = await PublicColumnsAsync(database);
            var identities = await RowsAsync(database, "SELECT list_id || '|' || member_count || '|' || created_at_utc_ticks FROM vote_member_lists ORDER BY list_id");

            await PostgresCollectionAttemptStore.FromConnectionString(database).MigrateAsync();

            Assert.Equal([$"{First}|8|2|2|1|1|2", $"{Second}|1|0|0|0|0|1"], await CountsAsync(database));
            Assert.Equal(entries, await EntriesAsync(database));
            Assert.Equal(identities, await RowsAsync(database, "SELECT list_id || '|' || member_count || '|' || created_at_utc_ticks FROM vote_member_lists ORDER BY list_id"));
            Assert.Equal(columns, await PublicColumnsAsync(database));
            await ExecuteAsync(database, "SET ROLE civic_lens_public; SELECT yea, nay, present, not_voting FROM public_api.votes LIMIT 0");

            await db.GetService<IMigrator>().MigrateAsync(previous);
            Assert.Equal(entries, await EntriesAsync(database));
            Assert.Equal(columns, await PublicColumnsAsync(database));
            Assert.Equal(["false"], await RowsAsync(database, "SELECT has_any_column_privilege('civic_lens_public_owner', 'vote_member_lists', 'SELECT')::text"));
            await PostgresCollectionAttemptStore.FromConnectionString(database).MigrateAsync();
            Assert.Equal([$"{First}|8|2|2|1|1|2", $"{Second}|1|0|0|0|0|1"], await CountsAsync(database));
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task IncompleteLegacyListRefusesUpgradeAndRollsBackWithoutChangingItsEvidence()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var db = Context(database);
            var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, Migration) < 0);
            await db.GetService<IMigrator>().MigrateAsync(previous);
            await SeedListAsync(database, First, ["Yea"], declaredCount: 2);
            var entries = await EntriesAsync(database);

            var failure = await Assert.ThrowsAsync<PostgresException>(() =>
                PostgresCollectionAttemptStore.FromConnectionString(database).MigrateAsync());

            Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
            Assert.Equal("ck_vote_member_lists_totals", failure.ConstraintName);
            Assert.Equal(entries, await EntriesAsync(database));
            Assert.Equal(["2"], await RowsAsync(database, "SELECT member_count::text FROM vote_member_lists"));
            Assert.Equal(["0"], await RowsAsync(database, "SELECT count(*) FROM information_schema.columns WHERE table_name = 'vote_member_lists' AND column_name = 'yea_count'"));
            Assert.Equal(previous, (await db.Database.GetAppliedMigrationsAsync()).Last());
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task InvalidMaximumSmallintCountsFailTheTotalConstraintWithoutArithmeticOverflow()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await ExecuteAsync(database, $"""
                INSERT INTO vote_member_lists (list_id, member_count, yea_count, nay_count, present_count, not_voting_count, other_count, created_at_utc_ticks)
                VALUES ('{First}', 1, 1, 0, 0, 0, 0, 1)
                """);

            var failure = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(database, """
                UPDATE vote_member_lists SET yea_count = 32767, nay_count = 32767, present_count = 32767,
                    not_voting_count = 32767, other_count = 32767
                """));

            Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
            Assert.Equal("ck_vote_member_lists_totals", failure.ConstraintName);
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    private static CollectionAttemptDbContext Context(string database) =>
        new(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);

    private static async Task SeedListAsync(string database, string list, string[] casts, int? declaredCount = null)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SET session_replication_role = replica;
            INSERT INTO vote_member_lists (list_id, member_count, created_at_utc_ticks) VALUES (@list, @count, 1);
            INSERT INTO vote_member_list_entries (list_id, position, person_id, bioguide_id, vote_cast, citation_extraction_id, citation_start, citation_length)
            SELECT @list, (member.entry_position - 1)::smallint, 'member-' || member.entry_position, 'B' || lpad(member.entry_position::text, 6, '0'),
                   member.vote_cast, 'ext', 0, 1
              FROM unnest(@casts::text[]) WITH ORDINALITY AS member(vote_cast, entry_position);
            """, connection);
        command.Parameters.AddWithValue("list", list);
        command.Parameters.AddWithValue("count", (short)(declaredCount ?? casts.Length));
        command.Parameters.AddWithValue("casts", casts);
        await command.ExecuteNonQueryAsync();
    }

    private static Task<string[]> CountsAsync(string database) => RowsAsync(database, """
        SELECT list_id || '|' || member_count || '|' || yea_count || '|' || nay_count || '|' || present_count || '|' || not_voting_count || '|' || other_count
          FROM vote_member_lists ORDER BY list_id
        """);

    private static Task<string[]> EntriesAsync(string database) => RowsAsync(database, """
        SELECT list_id || '|' || position || '|' || person_id || '|' || bioguide_id || '|' || vote_cast || '|' || citation_extraction_id || '|' || citation_start || '|' || citation_length
          FROM vote_member_list_entries ORDER BY list_id, position
        """);

    private static Task<string[]> PublicColumnsAsync(string database) => RowsAsync(database, """
        SELECT column_name || '|' || data_type || '|' || ordinal_position
          FROM information_schema.columns WHERE table_schema = 'public_api' AND table_name = 'votes' ORDER BY ordinal_position
        """);

    private static async Task ExecuteAsync(string database, string sql)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task<string[]> RowsAsync(string database, string sql)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        var rows = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) rows.Add(Convert.ToString(reader.GetValue(0))!);
        return [.. rows];
    }
}
