using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Publication;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresPublishedRecordsTests(PostgresCollection postgres) : IAsyncLifetime
{
    private const string Migration = "PublishedRecords";
    private static readonly string A = new('a', 64);
    private static readonly string B = new('b', 64);
    private static readonly DateTimeOffset First = new(2026, 3, 1, 8, 0, 0, TimeSpan.Zero);
    private string connectionString = null!;
    private PostgresPublicationStore publications = null!;
    private int commitCount;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        publications = new PostgresPublicationStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options), TestCodeVersion.Current);
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    [Fact]
    public async Task ACommitWritesTheFirstTimeOfEachAddedRecordAndTheReadReturnsOnlyTheNamedRecords()
    {
        await SeedActionsAsync(A, B);

        await PublishAsync(1, null, [(A, First)]);

        var read = await publications.ReadFirstPublishedAsync([Reference(A), Reference(B)], default);
        Assert.Equal(First, read[Reference(A)]);
        Assert.False(read.ContainsKey(Reference(B)));
        Assert.Empty(await publications.ReadFirstPublishedAsync([], default));
    }

    [Fact]
    public async Task ARollbackAndAPublishOfTheRecordAgainKeepTheFirstTimeBecauseTheTableHasInsertsOnly()
    {
        await SeedActionsAsync(A, B);
        await PublishAsync(1, null, [(B, First)]);
        await PublishAsync(2, 1, [(A, First.AddDays(1))]);
        await publications.ActivateAsync(1, (_, _) => Task.CompletedTask, default);

        await PublishAsync(3, 1, [(A, First.AddDays(1))]);

        Assert.Equal(First.AddDays(1), (await publications.ReadFirstPublishedAsync([Reference(A)], default))[Reference(A)]);
        Assert.Equal(["action|" + A + "|" + First.AddDays(1).UtcTicks, "action|" + B + "|" + First.UtcTicks],
            await ReadAsync("SELECT kind || '|' || record_id || '|' || first_published_at_utc_ticks FROM published_records ORDER BY 1"));
    }

    [Fact]
    public async Task ACommitWhoseTimeDiffersFromTheStoredTimeConflictsAndWritesNoReleaseBecauseTheReleaseFileWouldSplitTheFirstTime()
    {
        await SeedActionsAsync(A);
        await publications.AddFirstPublishedAsync([new(PublishedRecordKind.Action, A, First)], default);

        await Assert.ThrowsAsync<PublicationConflictException>(() => PublishAsync(1, null, [(A, First.AddDays(3))]));

        Assert.Empty(await ReadAsync("SELECT release_number FROM publication_releases"));
    }

    [Fact]
    public async Task TheBackfillInsertsMissingRowsCountsThemAndLeavesAnExistingRowAlone()
    {
        await SeedActionsAsync(A);
        await PublishAsync(1, null, [(A, First)]);

        var added = await publications.AddFirstPublishedAsync(
            [new(PublishedRecordKind.Action, A, First.AddDays(9)), new(PublishedRecordKind.Action, B, First.AddDays(2))], default);

        Assert.Equal(1, added);
        var read = await publications.ReadFirstPublishedAsync([Reference(A), Reference(B)], default);
        Assert.Equal((First, First.AddDays(2)), (read[Reference(A)], read[Reference(B)]));
        Assert.Equal(0, await publications.AddFirstPublishedAsync([], default));
    }

    [Theory]
    [InlineData("'action', 'xyz', 1")]
    [InlineData("'draft', 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 1")]
    [InlineData("'action', 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', -1")]
    [InlineData("'action', 'aaaaaaaaaaaaaaaaaaaaaaaaaaaaaaaa', 3155378976000000000")]
    public async Task TheTableRefusesAnUnknownKindABadRecordIdAndATimeOutsideTheTickRange(string values)
    {
        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            ExecuteAsync($"INSERT INTO published_records (kind, record_id, first_published_at_utc_ticks) VALUES ({values})"));

        Assert.Equal(PostgresErrorCodes.CheckViolation, failure.SqlState);
    }

    [Fact]
    public async Task TheMigrationCreatesTheTableAndGoesDownByDroppingItBecauseDownMustWorkForARollback()
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        var migrations = db.Database.GetMigrations().ToList();
        var migration = migrations.Single(id => id.EndsWith("_" + Migration, StringComparison.Ordinal));
        Assert.Equal(["published_records"], await ReadAsync("SELECT table_name FROM information_schema.tables WHERE table_name = 'published_records'"));

        await db.GetService<IMigrator>().MigrateAsync(migrations[migrations.IndexOf(migration) - 1]);

        Assert.Empty(await ReadAsync("SELECT table_name FROM information_schema.tables WHERE table_name = 'published_records'"));
        await db.Database.MigrateAsync();
        Assert.Single(await ReadAsync("SELECT table_name FROM information_schema.tables WHERE table_name = 'published_records'"));
    }

    private static PublishedRecordReference Reference(string id) => new(PublishedRecordKind.Action, id);

    private Task PublishAsync(int number, int? baseNumber, (string Id, DateTimeOffset FirstPublishedAt)[] added) =>
        publications.CommitAsync("auth0|owner", new PublicationCommit(number, baseNumber, $"{number:D6}-{Guid.NewGuid():N}",
            [.. added.Select(record => new PublishedRevisionBinding(PublishedRecordKind.Action, record.Id, 1))],
            [.. added.Select(record => new ReviewStateExpectation(PublishedRecordKind.Action, record.Id, 1, 1))])
        {
            Withdrawals = [],
            FirstPublications = [.. added.Select(record => new PublishedFirstPublication(PublishedRecordKind.Action, record.Id, record.FirstPublishedAt))]
        }, $"key-{++commitCount}", Convert.ToHexStringLower(BitConverter.GetBytes(commitCount)).PadLeft(64, '0'), default);

    private async Task SeedActionsAsync(params string[] ids)
    {
        foreach (var (id, index) in ids.Select((id, index) => (id, index)))
            await ExecuteAsync($"""
                SET session_replication_role = replica;
                INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, created_at_utc_ticks,
                    last_revised_at_utc_ticks, review_state_version, review_status)
                VALUES ('{id}', 'congress-bills', 119, 'hr', {index + 1}, 1, 0, 0, 1, 'Approved')
                """);
    }

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<List<string>> ReadAsync(string sql) =>
        [.. (await PublishedRecordStateReader.ReadAsync(connectionString, sql)).Select(row => row[0])];
}
