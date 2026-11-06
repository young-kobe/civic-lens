using CivicLens.Application.Publication;
using CivicLens.Infrastructure.Collection;
using CivicLens.Infrastructure.Publication;
using CivicLens.Publication.Contracts;
using CivicLens.Tests.Fixtures;
using CivicLens.Tests.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Publication;

[Collection(PostgresCollection.Name)]
[Trait("Category", "Postgres")]
public sealed class PostgresReleaseBillStatusesTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly string Action1 = new('a', 64);
    private static readonly string Action2 = new('b', 64);

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
    public async Task ACommitPinsTheStatusesOfItsReleaseAndALaterReleaseLeavesTheEarlierRowsAsTheyWereBecauseTheApiMustShowWhatEachReleaseShowed()
    {
        await CommitAsync(1, null, [Status(Action1, "Received in the Senate.", 5), Status(Action2, "Referred to the Committee on Rules.", 6)]);
        var before = await ReadAsync("SELECT release_number || '|' || left(record_id, 1) || '|' || latest_action_text || '|' || latest_action_date || '|' || read_at_utc_ticks FROM release_bill_statuses ORDER BY 1");

        await CommitAsync(2, 1, [Status(Action1, "Became Public Law No: 119-21.", 7)]);

        Assert.Equal(["1|a|Received in the Senate.|2026-02-13|5", "1|b|Referred to the Committee on Rules.|2026-02-13|6"], before);
        Assert.Equal(["1|a|Received in the Senate.|2026-02-13|5", "1|b|Referred to the Committee on Rules.|2026-02-13|6", "2|a|Became Public Law No: 119-21.|2026-02-13|7"],
            await ReadAsync("SELECT release_number || '|' || left(record_id, 1) || '|' || latest_action_text || '|' || latest_action_date || '|' || read_at_utc_ticks FROM release_bill_statuses ORDER BY 1"));
    }

    [Fact]
    public async Task ARefusedCommitPinsNoStatusBecauseTheRowsShareTheTransactionOfTheRelease()
    {
        var stored = new DateTimeOffset(2026, 1, 1, 0, 0, 0, TimeSpan.Zero);
        await publications.AddFirstPublishedAsync([new(PublishedRecordKind.Action, Action1, stored)], default);
        var refused = new PublicationCommit(1, null, $"000001-{Guid.NewGuid():N}", [], [])
        {
            Withdrawals = [],
            AddedEntries = [PublishedEntryFixtures.Action(Action1, stored.AddDays(3))],
            Names = new ReleaseNames([], new ReleaseFacets([], [])),
            BillStatuses = [Status(Action1, "Received in the Senate.", 5)]
        };

        await Assert.ThrowsAsync<PublicationConflictException>(() => publications.CommitAsync("auth0|owner", refused, "key-refused", new string('0', 64), default));

        Assert.Empty(await ReadAsync("SELECT record_id FROM release_bill_statuses"));
    }

    [Fact]
    public async Task TheBackfillWritesOnlyTheRowsThatAReleaseLacksAndNeverChangesARowThatExists()
    {
        await CommitAsync(1, null, [Status(Action1, "Received in the Senate.", 5)]);

        var written = await publications.AddBillStatusesAsync(1, [Status(Action1, "Another text.", 9), Status(Action2, "Referred to the Committee on Rules.", 6)], default);

        Assert.Equal(1, written);
        Assert.Equal(["a|Received in the Senate.|5", "b|Referred to the Committee on Rules.|6"],
            await ReadAsync("SELECT left(record_id, 1) || '|' || latest_action_text || '|' || read_at_utc_ticks FROM release_bill_statuses ORDER BY 1"));
    }

    private static PublishedBillStatus Status(string record, string text, long ticks) =>
        new() { RecordId = record, Text = text, Date = new(2026, 2, 13), ObservedAtUtc = new DateTimeOffset(ticks, TimeSpan.Zero) };

    private Task CommitAsync(int number, int? baseNumber, PublishedBillStatus[] statuses) =>
        publications.CommitAsync("auth0|owner", new PublicationCommit(number, baseNumber, $"{number:D6}-{Guid.NewGuid():N}", [], [])
        {
            Withdrawals = [],
            Names = new ReleaseNames([], new ReleaseFacets([], [])),
            BillStatuses = [.. statuses]
        }, $"key-{++commitCount}", Convert.ToHexStringLower(BitConverter.GetBytes(commitCount)).PadLeft(64, '0'), default);

    private async Task<string[]> ReadAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string>();
        while (await reader.ReadAsync()) rows.Add(Convert.ToString(reader.GetValue(0))!);
        return [.. rows];
    }
}
