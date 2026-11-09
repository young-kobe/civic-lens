using CivicLens.Infrastructure.Collection;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Infrastructure.Collection;
using CivicLens.Tests.PublicApi.Bills;
using CivicLens.Tests.PublicApi.Votes;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;
using NpgsqlTypes;

namespace CivicLens.Tests.Infrastructure.PublicApi;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class ReleaseHomeIssueCountsMigrationTests(PostgresCollection postgres)
{
    private const string Migration = "20261109182200_ReleaseHomeIssueCounts";

    [Fact]
    public async Task UpgradeAddsSnapshotViewColumnsAndGrantsThemThenDowngradeRestoresPreviousShape()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);
            var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, Migration) < 0);
            var before = await ColumnsAsync(database);
            await db.GetService<IMigrator>().MigrateAsync(previous);
            var downgraded = await ColumnsAsync(database);
            var store = PostgresCollectionAttemptStore.FromConnectionString(database);
            await store.MigrateAsync();

            var after = await ColumnsAsync(database);
            Assert.Equal(downgraded, after[..downgraded.Length]);
            Assert.Equal(["count_congress|integer|5", "substantive_votes|bigint|6", "bills|bigint|7"], after[^3..]);
            await ReadAsPublicAsync(database);
            await AssertInvalidRowsAsync(database);

            await db.GetService<IMigrator>().MigrateAsync(previous);
            Assert.Equal(downgraded, await ColumnsAsync(database));
            await store.MigrateAsync();
            Assert.Equal(before, await ColumnsAsync(database));
            await ReadAsPublicAsync(database);
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task BackfillCountsEachHistoricalRevisionAndExcludesWithdrawnAndIneligibleRecords()
    {
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);
            var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, Migration) < 0);
            await db.GetService<IMigrator>().MigrateAsync(previous);
            var published = new DateTimeOffset(2026, 6, 2, 0, 0, 0, TimeSpan.Zero);
            await ViewsSeed.ActivateReleaseAsync(database, 1, published, recordCount: 9);
            await ViewsSeed.ActivateReleaseAsync(database, 2, published.AddDays(1), recordCount: 8);
            await ViewsSeed.AddVotesAsync(database,
                new SeededVote('a', "2026-05-01") { FirstRelease = 2, ActiveRevision = 2, EarlierIssues = ["health"], Issues = ["climate"] },
                new SeededVote('b', "2026-05-02") { Kind = "Rule", Issues = ["health"] },
                new SeededVote('c', "2026-05-03") { LastRelease = 1, Issues = ["health"] },
                new SeededVote('d', "2026-05-04") { Congress = 118, Issues = ["health"] },
                new SeededVote('e', "2026-05-05") { UntrackedIssues = ["health"] });
            await BillsSeed.AddBillsAsync(database,
                new SeededBill('1', "2026-05-01") { LastRelease = 1, Issues = ["health"] },
                new SeededBill('2', "2026-05-02") { FirstRelease = 2, Issues = ["health"] },
                new SeededBill('3', "2026-05-03") { Congress = 118, Issues = ["health"] },
                new SeededBill('4', "2026-05-04") { Sponsorships = [], Issues = ["health"] },
                new SeededBill('5', "2026-05-05") { UntrackedIssues = ["health"] });
            await ViewsSeed.ExecuteAsync(database, """
                INSERT INTO publication_record_states (kind, record_id, first_release, revision_number, last_release)
                VALUES ('vote', 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 1, 1, 1);
                INSERT INTO release_issues (release_number, issue_id, name, definition)
                SELECT release, issue, issue, 'Definition'
                  FROM unnest(ARRAY[1,2]) AS releases(release)
                 CROSS JOIN unnest(ARRAY['health','climate','empty']) AS issues(issue);
                """);

            await PostgresCollectionAttemptStore.FromConnectionString(database).MigrateAsync();

            await using var connection = new NpgsqlConnection(database);
            await connection.OpenAsync();
            await using var command = new NpgsqlCommand("""
                SELECT release_number || '|' || issue_id || '|' || count_congress || '|' || substantive_votes || '|' || bills
                  FROM release_issues ORDER BY release_number, issue_id
                """, connection);
            var counts = new List<string>();
            await using var reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync()) counts.Add(reader.GetString(0));
            Assert.Equal(["1|climate|119|0|0", "1|empty|119|0|0", "1|health|119|2|1",
                "2|climate|119|1|0", "2|empty|119|0|0", "2|health|119|0|1"], counts);
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    [Fact]
    public async Task BackfillUsesTheCongressCalendarAtJanuaryAndSupportedStartBoundaries()
    {
        var days = new[]
        {
            new DateOnly(1934, 12, 31), new DateOnly(1935, 1, 1), new DateOnly(1935, 1, 2), new DateOnly(1935, 1, 3),
            new DateOnly(2027, 1, 1), new DateOnly(2027, 1, 2), new DateOnly(2027, 1, 3),
            new DateOnly(2028, 1, 1), new DateOnly(2028, 1, 2), new DateOnly(2028, 1, 3)
        };
        var database = await postgres.CreateMigratedDatabaseAsync();
        try
        {
            await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(database).Options);
            var previous = db.Database.GetMigrations().Last(id => string.CompareOrdinal(id, Migration) < 0);
            await db.GetService<IMigrator>().MigrateAsync(previous);
            await SeedHistoricalReleasesAsync(database, days);
            await PostgresCollectionAttemptStore.FromConnectionString(database).MigrateAsync();
            var actual = await CongressSnapshotsAsync(database);
            Assert.Equal(days.Select(CongressCalendar.CongressOn), actual);
        }
        finally { await postgres.DropDatabaseAsync(database); }
    }

    private static async Task SeedHistoricalReleasesAsync(string database, DateOnly[] days)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        for (var index = 0; index < days.Length; index++)
        {
            var release = index + 1;
            var at = days[index].ToDateTime(TimeOnly.MinValue, DateTimeKind.Utc);
            await using var command = new NpgsqlCommand("""
                INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active, code_version)
                VALUES (@release, @directory, 'migration-test', @ticks, 0, false, repeat('a', 40));
                INSERT INTO release_issues (release_number, issue_id, name, definition)
                VALUES (@release, 'health', 'Health', 'Historical empty issue');
                """, connection);
            command.Parameters.AddWithValue("release", release);
            command.Parameters.AddWithValue("directory", $"{release:D6}-{new string('a', 32)}");
            command.Parameters.AddWithValue("ticks", at.Ticks);
            await command.ExecuteNonQueryAsync();
        }
    }

    private static async Task<int?[]> CongressSnapshotsAsync(string database)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT count_congress FROM release_issues ORDER BY release_number", connection);
        var actual = new List<int?>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) actual.Add(reader.IsDBNull(0) ? null : reader.GetInt32(0));
        return [.. actual];
    }

    private static async Task ReadAsPublicAsync(string database)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SET ROLE civic_lens_public; SELECT count_congress, substantive_votes, bills FROM public_api.release_issues LIMIT 0", connection);
        await command.ExecuteNonQueryAsync();
    }

    private static async Task AssertInvalidRowsAsync(string database)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using (var seed = new NpgsqlCommand("""
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, is_active, record_count, code_version)
            VALUES (991, '991000-aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 'test', 638845920000000000, false, 0, repeat('a', 40))
            """, connection))
            await seed.ExecuteNonQueryAsync();

        foreach (var (issue, congress, votes, bills) in new[] { ("partial", 119, (long?)null, (long?)0), ("negative", 119, (long?)-1, (long?)0) })
        {
            await using var command = new NpgsqlCommand("""
                INSERT INTO release_issues (release_number, issue_id, name, count_congress, substantive_votes, bills)
                VALUES (991, @issue, @issue, @congress, @votes, @bills)
                """, connection);
            command.Parameters.AddWithValue("issue", issue);
            command.Parameters.AddWithValue("congress", congress);
            command.Parameters.Add(new NpgsqlParameter("votes", NpgsqlDbType.Bigint) { Value = votes is null ? DBNull.Value : votes.Value });
            command.Parameters.Add(new NpgsqlParameter("bills", NpgsqlDbType.Bigint) { Value = bills!.Value });
            var invalid = await Assert.ThrowsAsync<PostgresException>(() => command.ExecuteNonQueryAsync());
            Assert.Equal("23514", invalid.SqlState);
        }
    }

    private static async Task<string[]> ColumnsAsync(string database)
    {
        await using var connection = new NpgsqlConnection(database);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("SELECT column_name || '|' || data_type || '|' || ordinal_position FROM information_schema.columns WHERE table_schema = 'public_api' AND table_name = 'release_issues' ORDER BY ordinal_position", connection);
        var columns = new List<string>();
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync()) columns.Add(reader.GetString(0));
        return [.. columns];
    }
}
