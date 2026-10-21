using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class VoteChamberMigrationTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string PreviousMigration = "20261109120000_ReaderReports";
    private string connectionString = null!;

    public async Task InitializeAsync() => connectionString = await postgres.CreateEmptyDatabaseAsync();

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task ExistingVoteRowsBecomeHouseRowsInTheRenamedTables()
    {
        await using var db = Context();
        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);
        await ExecuteAsync($"""
            SET session_replication_role = replica;
            INSERT INTO house_votes (detail_job_id, source_id, congress, session_number, roll_call_number, update_date_utc_ticks, extraction_id)
            VALUES ('detail', 'source', 119, 1, 100, 1, '{new string('d', 64)}');
            INSERT INTO house_vote_candidates (candidate_id, source_id, congress, session_number, roll_call_number, update_date_utc_ticks,
                detail_job_id, members_job_id, detail_extraction_id, members_extraction_id, configuration_revision_id, created_at_utc_ticks)
            VALUES ('{new string('a', 64)}', 'source', 119, 1, 100, 1, 'detail', 'members', '{new string('d', 64)}', '{new string('e', 64)}',
                '{new string('c', 64)}', 1);
            INSERT INTO vote_drafts (record_id, source_id, congress, session_number, roll_call_number, current_revision_number,
                review_state_version, review_status, created_at_utc_ticks)
            VALUES ('{new string('b', 64)}', 'source', 119, 1, 100, 1, 0, 'AwaitingReview', 1);
            """);

        await db.Database.MigrateAsync();

        Assert.Equal([["House"]], await ReadAsync("SELECT chamber FROM vote_versions"));
        Assert.Equal([["House"]], await ReadAsync("SELECT chamber FROM vote_candidates"));
        Assert.Equal([["House"]], await ReadAsync("SELECT chamber FROM vote_drafts"));
        Assert.Equal([[""]], await ReadAsync("SELECT coalesce(to_regclass('house_votes')::text, '') UNION SELECT coalesce(to_regclass('house_vote_candidates')::text, '')"));
        Assert.False(db.Database.HasPendingModelChanges());
    }

    [Fact]
    public async Task NoConstraintOrIndexKeepsTheHouseOnlyTableName()
    {
        await using var db = Context();

        await db.Database.MigrateAsync();

        Assert.Empty(await ReadAsync("SELECT conname FROM pg_constraint WHERE conname ILIKE '%house_vote%' AND contype <> 'n'"));
        Assert.Empty(await ReadAsync("SELECT indexname FROM pg_indexes WHERE indexname ILIKE '%house_vote%'"));
    }

    [Fact]
    public async Task TheSameRollNumberCanExistInBothChambersButNotTwiceInOne()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();

        await ExecuteAsync(Draft('a', "House"));
        await ExecuteAsync(Draft('b', "Senate"));

        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(Draft('c', "Senate")));
        Assert.Equal(PostgresErrorCodes.UniqueViolation, duplicate.SqlState);
        var unknown = await Assert.ThrowsAsync<PostgresException>(() => ExecuteAsync(Draft('d', "Congress")));
        Assert.Equal(PostgresErrorCodes.CheckViolation, unknown.SqlState);
    }

    [Fact]
    public async Task RevertingRestoresTheHouseTableNames()
    {
        await using var db = Context();
        await db.Database.MigrateAsync();

        await db.GetService<IMigrator>().MigrateAsync(PreviousMigration);

        Assert.Equal([["house_votes"], ["house_vote_candidates"]],
            await ReadAsync("SELECT to_regclass('house_votes')::text UNION ALL SELECT to_regclass('house_vote_candidates')::text"));
        Assert.Empty(await ReadAsync("SELECT conname FROM pg_constraint WHERE conname ~* '(?<!house_)vote_candidates|vote_versions' AND contype <> 'n'"));
    }

    private static string Draft(char id, string chamber) => $"""
        INSERT INTO vote_drafts (record_id, source_id, chamber, congress, session_number, roll_call_number, current_revision_number,
            review_state_version, review_status, created_at_utc_ticks)
        VALUES ('{new string(id, 64)}', 'source', '{chamber}', 119, 1, 100, 1, 0, 'AwaitingReview', 1)
        """;

    private CollectionAttemptDbContext Context() => new(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);

    private async Task<List<string[]>> ReadAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string[]>();
        while (await reader.ReadAsync())
            rows.Add([.. Enumerable.Range(0, reader.FieldCount).Select(index => reader.GetValue(index).ToString()!)]);
        return rows;
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
