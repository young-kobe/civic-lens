using System.Diagnostics;
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
public sealed class PostgresPublicationRecordStateTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly string A = new('a', 64);
    private static readonly string B = new('b', 64);
    private static readonly string C = new('c', 64);
    private static readonly string X = new('d', 64);
    private string connectionString = null!;
    private PostgresPublicationStore publications = null!;
    private int commitCount;

    public async Task InitializeAsync()
    {
        connectionString = await postgres.CreateMigratedDatabaseAsync();
        publications = new PostgresPublicationStore(new PooledDbContextFactory<CollectionAttemptDbContext>(
            new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options));
    }

    public Task DisposeAsync() => postgres.DropDatabaseAsync(connectionString);

    // Readers will answer "what did release R hold" from the table, so it must equal records_json for every release.
    // The rollback makes the highest release differ from the active one. Release 4 withdraws X, release 3 comes back,
    // and release 5 keeps X. A diff against the active release would never reopen X.
    [Fact]
    public async Task TheTableMatchesTheRecordsOfEveryReleaseAfterWithdrawalReturnRevisionChangeAndRollback()
    {
        await SeedActionsAsync(A, B, C, X);
        await PublishAsync(1, null, [(A, 1), (B, 1), (X, 1)], added: [A, B, X]);
        await PublishAsync(2, 1, [(A, 1), (B, 1), (X, 1), (C, 1)], added: [C]);
        await SetRevisionAsync(A, 2);
        await SetStatusAsync(B, "ApprovalWithdrawn");
        await PublishAsync(3, 2, [(A, 2), (X, 1), (C, 1)], added: [A], withdrawn: [B]);
        await SetStatusAsync(X, "ApprovalWithdrawn");
        await PublishAsync(4, 3, [(A, 2), (C, 1)], withdrawn: [X]);
        await publications.ActivateAsync(3, (_, _) => Task.CompletedTask, default);
        await SetStatusAsync(B, "Approved");
        await PublishAsync(5, 3, [(A, 2), (X, 1), (C, 1), (B, 1)], added: [B]);
        await SetRevisionAsync(A, 3);
        await PublishAsync(6, 5, [(A, 3), (X, 1), (C, 1), (B, 1)], added: [A]);

        await PublishedRecordStateReader.AssertEveryReleaseMatchesAsync(connectionString);
        Assert.Equal(["1|3", "5|NULL"], await RangesAsync(X));
        Assert.Equal(["1|2", "3|5", "6|NULL"], await RangesAsync(A));
    }

    [Fact]
    public async Task AnUnchangedRecordKeepsItsOneRowBecauseOnlyChangesWriteToTheTable()
    {
        await SeedActionsAsync(A, C);
        await PublishAsync(1, null, [(A, 1)], added: [A]);
        await PublishAsync(2, 1, [(A, 1), (C, 1)], added: [C]);
        await PublishAsync(3, 2, [(A, 1), (C, 1)]);

        Assert.Equal(["1|NULL"], await RangesAsync(A));
        Assert.Equal(["2|NULL"], await RangesAsync(C));
    }

    [Fact]
    public async Task AReplayWritesNoRowsBecauseTheReleaseAlreadyExists()
    {
        await SeedActionsAsync(A);
        var commit = Commit(1, null, [(A, 1)], [A], []);
        await publications.CommitAsync("auth0|owner", commit, "same-key", new string('1', 64), default);
        var before = await AllRowsAsync();

        await publications.CommitAsync("auth0|owner", commit, "same-key", new string('1', 64), default);

        Assert.Equal(before, await AllRowsAsync());
    }

    [Fact]
    public async Task AConflictedCommitLeavesNoRowsBecauseTheStateWritesJoinTheCommitTransaction()
    {
        await SeedActionsAsync(A, C);
        await PublishAsync(1, null, [(A, 1)], added: [A]);
        var before = await AllRowsAsync();

        await Assert.ThrowsAsync<PublicationConflictException>(() => publications.CommitAsync("auth0|owner",
            Commit(3, 1, [(A, 1), (C, 1)], [C], []), "late-key", new string('2', 64), default));

        Assert.Equal(before, await AllRowsAsync());
    }

    [Fact]
    public async Task ACommitOfFifteenThousandRecordsStaysFastBecauseTheDiffReadsOneReleaseAndWritesInTwoStatements()
    {
        await ExecuteAsync("""
            INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, created_at_utc_ticks,
                last_revised_at_utc_ticks, review_state_version, review_status)
            SELECT md5(n::text) || md5('x' || n::text), 'bulk', 119, 'hr', n, 1, 0, 0, 1, 'Approved' FROM generate_series(1, 15000) n
            """);
        var ids = (await PublishedRecordStateReader.ReadAsync(connectionString, "SELECT record_id FROM action_drafts")).Select(row => row[0]).ToArray();

        var opening = await TimeAsync(() => PublishAsync(1, null, [.. ids.Select(id => (id, 1))], added: ids));
        await ExecuteAsync("UPDATE action_drafts SET current_revision_number = 2");
        var revising = await TimeAsync(() => PublishAsync(2, 1, [.. ids.Select(id => (id, 2))], added: ids));

        Assert.True(opening < TimeSpan.FromSeconds(5), $"The first commit took {opening}.");
        Assert.True(revising < TimeSpan.FromSeconds(5), $"The revising commit took {revising}.");
        await PublishedRecordStateReader.AssertEveryReleaseMatchesAsync(connectionString);
    }

    private static async Task<TimeSpan> TimeAsync(Func<Task> action)
    {
        var clock = Stopwatch.StartNew();
        await action();
        return clock.Elapsed;
    }

    private async Task<List<string>> RangesAsync(string recordId) =>
        [.. (await PublishedRecordStateReader.ReadAsync(connectionString, $"""
            SELECT first_release || '|' || COALESCE(last_release::text, 'NULL') FROM publication_record_states
            WHERE record_id = '{recordId}' ORDER BY first_release
            """)).Select(row => row[0])];

    private async Task<List<string>> AllRowsAsync() =>
        [.. (await PublishedRecordStateReader.ReadAsync(connectionString, """
            SELECT kind || record_id || revision_number || '|' || first_release || '|' || COALESCE(last_release::text, 'NULL')
            FROM publication_record_states ORDER BY 1
            """)).Select(row => row[0])];

    private Task PublishAsync(int number, int? baseNumber, (string Id, int Revision)[] records, string[]? added = null, string[]? withdrawn = null) =>
        publications.CommitAsync("auth0|owner", Commit(number, baseNumber, records, added ?? [], withdrawn ?? []),
            $"key-{++commitCount}", Convert.ToHexStringLower(BitConverter.GetBytes(commitCount)).PadLeft(64, '0'), default);

    private static PublicationCommit Commit(int number, int? baseNumber, (string Id, int Revision)[] records, string[] added, string[] withdrawn) =>
        new(number, baseNumber, $"{number:D6}-{Guid.NewGuid():N}",
            [.. records.Select(record => new PublishedRevisionBinding(PublishedRecordKind.Action, record.Id, record.Revision))],
            [.. records.Where(record => added.Contains(record.Id))
                .Select(record => new ReviewStateExpectation(PublishedRecordKind.Action, record.Id, record.Revision, 1))])
        {
            WithdrawnRecords = [.. withdrawn.Select(id => new PublishedRecordReference(PublishedRecordKind.Action, id))]
        };

    private async Task SeedActionsAsync(params string[] ids)
    {
        foreach (var (id, index) in ids.Select((id, index) => (id, index)))
            await ExecuteAsync($"""
                INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, created_at_utc_ticks,
                    last_revised_at_utc_ticks, review_state_version, review_status)
                VALUES ('{id}', 'congress-bills', 119, 'hr', {index + 1}, 1, 0, 0, 1, 'Approved')
                """);
    }

    private Task SetRevisionAsync(string id, int revision) =>
        ExecuteAsync($"UPDATE action_drafts SET current_revision_number = {revision} WHERE record_id = '{id}'");

    private Task SetStatusAsync(string id, string status) =>
        ExecuteAsync($"UPDATE action_drafts SET review_status = '{status}' WHERE record_id = '{id}'");

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }
}
