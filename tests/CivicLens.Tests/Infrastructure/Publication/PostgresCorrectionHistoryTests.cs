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
public sealed class PostgresCorrectionHistoryTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly string A = new('a', 64);
    private static readonly string B = new('b', 64);
    private static readonly DateTimeOffset PublishedAt = new DateTimeOffset(2026, 4, 5, 6, 7, 8, TimeSpan.Zero).AddTicks(47);
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
    public async Task ConcurrentEquivalentPreparationStoresOneExactOrderedBody()
    {
        var record = Binding(A, 10);
        var corrections = OrderedCorrections();
        var history = new PublicationCorrectionHistory(record, corrections);

        var results = await Task.WhenAll(Enumerable.Range(0, 3)
            .Select(_ => publications.PrepareCorrectionHistoriesAsync([history], default)));

        var ids = results.Select(result => Assert.Single(result).HistoryId).Distinct(StringComparer.Ordinal).ToArray();
        Assert.Single(ids);
        Assert.Equal(1L, await ReadInt64Async(
            "SELECT count(*) FROM published_correction_histories WHERE history_id = @history_id", ("history_id", ids[0])));
        Assert.Equal((long)corrections.Length, await ReadInt64Async(
            "SELECT correction_count FROM published_correction_histories WHERE history_id = @history_id", ("history_id", ids[0])));

        var stored = await ReadCorrectionRowsAsync(ids[0]);
        Assert.Collection(stored,
            row => AssertCorrection(row, 0, corrections[0]),
            row => AssertCorrection(row, 1, corrections[1]),
            row => AssertCorrection(row, 2, corrections[2]));
    }

    [Fact]
    public async Task PreparationRefusesAnIncompleteExistingBodyWithoutRepairingIt()
    {
        var history = new PublicationCorrectionHistory(Binding(A, 10), OrderedCorrections());
        var binding = Assert.Single(await publications.PrepareCorrectionHistoriesAsync([history], default));
        await ExecuteAsync("DELETE FROM published_correction_history_entries WHERE history_id = @history_id AND position = 1",
            ("history_id", binding.HistoryId));

        await Assert.ThrowsAsync<InvalidDataException>(() => publications.PrepareCorrectionHistoriesAsync([history], default));

        Assert.Equal(2L, await ReadInt64Async(
            "SELECT count(*) FROM published_correction_history_entries WHERE history_id = @history_id", ("history_id", binding.HistoryId)));
        Assert.Equal(3L, await ReadInt64Async(
            "SELECT correction_count FROM published_correction_histories WHERE history_id = @history_id", ("history_id", binding.HistoryId)));
    }

    [Fact]
    public async Task OriginalOffsetParticipatesInIdentityEvenWhenUtcInstantMatches()
    {
        var record = Binding(A, 10);
        var utc = new DateTimeOffset(2026, 4, 5, 6, 7, 8, TimeSpan.Zero).AddTicks(47);
        var east = utc.ToOffset(TimeSpan.FromHours(5));
        var first = Correction(9, utc, "same note");
        var second = Correction(9, east, "same note");

        var bindings = await publications.PrepareCorrectionHistoriesAsync(
            [new(record, [first]), new(record, [second])], default);

        Assert.Equal(utc.UtcTicks, east.UtcTicks);
        Assert.NotEqual(bindings[0].HistoryId, bindings[1].HistoryId);
        var offsets = await ReadOffsetsAsync(bindings.Select(binding => binding.HistoryId).ToArray());
        Assert.Equal([0, 300], offsets.Order().ToArray());
    }

    [Fact]
    public async Task SameRecordRevisionCanBindDifferentVerifiedHistoriesInDifferentReleases()
    {
        await SeedActionsAsync((A, 10));
        var record = Binding(A, 10);
        var firstCorrection = Correction(9, PublishedAt.ToOffset(TimeSpan.FromHours(3)), "First correction");
        var secondCorrection = Correction(9, PublishedAt.ToOffset(TimeSpan.FromHours(-4)), "Second correction");
        var first = Assert.Single(await publications.PrepareCorrectionHistoriesAsync([new(record, [firstCorrection])], default));
        var second = Assert.Single(await publications.PrepareCorrectionHistoriesAsync([new(record, [secondCorrection])], default));

        await CommitAsync(1, null, [record], [new(PublishedRecordKind.Action, A, 10, 1)],
            [PublishedEntryFixtures.Action(A, PublishedAt, 10)], [first]);
        await CommitAsync(2, 1, [record], [], [], [second]);

        var firstRead = Assert.Single(await publications.ReadCorrectionHistoriesAsync(1, [record], default));
        var secondRead = Assert.Single(await publications.ReadCorrectionHistoriesAsync(2, [record], default));
        Assert.Equal((first.HistoryId, "First correction", TimeSpan.FromHours(3)),
            (firstRead.Value.Binding.HistoryId, firstRead.Value.Corrections[0].Note, firstRead.Value.Corrections[0].PublishedAtUtc.Offset));
        Assert.Equal((second.HistoryId, "Second correction", TimeSpan.FromHours(-4)),
            (secondRead.Value.Binding.HistoryId, secondRead.Value.Corrections[0].Note, secondRead.Value.Corrections[0].PublishedAtUtc.Offset));

        var release = Assert.Single(await publications.ListAllAsync(default), item => item.ReleaseNumber == 1);
        await Assert.ThrowsAsync<InvalidDataException>(() => publications.CommitCorrectionHistoryBootstrapAsync(release, [second],
            _ => Task.FromResult<string?>(null), default));
        var unchanged = Assert.Single(await publications.ReadCorrectionHistoriesAsync(1, [record], default));
        Assert.Equal(first.HistoryId, unchanged.Value.Binding.HistoryId);
    }

    [Fact]
    public async Task EmptyHistoryIsReturnedWhileAMissingLegacyBindingIsOmitted()
    {
        await SeedActionsAsync((A, 1), (B, 1));
        var first = Binding(A, 1);
        var second = Binding(B, 1);
        var prepared = await publications.PrepareCorrectionHistoriesAsync(
            [new(first, []), new(second, [])], default);
        Assert.Equal(prepared[0].HistoryId, prepared[1].HistoryId);
        Assert.Equal(0L, await ReadInt64Async(
            "SELECT correction_count FROM published_correction_histories WHERE history_id = @history_id",
            ("history_id", prepared[0].HistoryId)));
        Assert.Equal(0L, await ReadInt64Async(
            "SELECT count(*) FROM published_correction_history_entries WHERE history_id = @history_id",
            ("history_id", prepared[0].HistoryId)));

        await CommitAsync(1, null, [first, second],
            [new(PublishedRecordKind.Action, A, 1, 1), new(PublishedRecordKind.Action, B, 1, 1)],
            [PublishedEntryFixtures.Action(A, PublishedAt), PublishedEntryFixtures.Action(B, PublishedAt)], prepared);
        await ExecuteAsync("DELETE FROM release_correction_histories WHERE release_number = 1 AND kind = 'action' AND record_id = @record_id",
            ("record_id", B));

        var read = await publications.ReadCorrectionHistoriesAsync(1, [first, second], default);

        Assert.True(read.TryGetValue(new(PublishedRecordKind.Action, A), out var empty));
        Assert.Empty(empty!.Corrections);
        Assert.Equal(prepared[0].HistoryId, empty.Binding.HistoryId);
        Assert.False(read.ContainsKey(new(PublishedRecordKind.Action, B)));
    }

    [Fact]
    public async Task BootstrapPageKeepsCarriedReleaseRowsButReadsTheirSharedRevisionFactsOnce()
    {
        await SeedActionsAsync((A, 10));
        var record = Binding(A, 10);
        var history = Assert.Single(await publications.PrepareCorrectionHistoriesAsync([new(record, [])], default));
        await CommitAsync(1, null, [record], [new(PublishedRecordKind.Action, A, 10, 1)],
            [PublishedEntryFixtures.Action(A, PublishedAt, 10)], [history]);
        await CommitAsync(2, 1, [record], [], [], [history]);
        await ExecuteAsync("DELETE FROM release_correction_histories WHERE kind = 'action' AND record_id = @record_id",
            ("record_id", A));

        var page = await publications.ReadCorrectionHistoryBootstrapPageAsync(null, 10, _ => Task.FromResult<string?>(null), default);

        Assert.Equal([1, 2], page.Candidates.Select(candidate => candidate.Release.ReleaseNumber).ToArray());
        Assert.All(page.Candidates, candidate => Assert.Equal(record, new PublishedRevisionBinding(candidate.Record.Kind,
            candidate.Record.RecordId, candidate.Record.RevisionNumber)));
        foreach (var candidate in page.Candidates)
        {
            var added = await publications.CommitCorrectionHistoryBootstrapAsync(candidate.Release, [history],
                _ => Task.FromResult<string?>(null), default);
            Assert.Equal((1, 0), added);
            var repeated = await publications.CommitCorrectionHistoryBootstrapAsync(candidate.Release, [history],
                _ => Task.FromResult<string?>(null), default);
            Assert.Equal((0, 1), repeated);
        }

        var first = Assert.Single(await publications.ReadCorrectionHistoriesAsync(1, [record], default));
        var second = Assert.Single(await publications.ReadCorrectionHistoriesAsync(2, [record], default));
        Assert.Equal(first.Value.Binding.HistoryId, second.Value.Binding.HistoryId);
        Assert.Empty(first.Value.Corrections);
        Assert.Empty(second.Value.Corrections);
    }

    [Fact]
    public async Task BootstrapPageReadsOneFirstPublicationForDifferentRetainedRevisionsOfTheSameRecord()
    {
        await SeedActionsAsync((A, 10));
        var firstRecord = Binding(A, 10);
        await CommitAsync(1, null, [firstRecord], [new(PublishedRecordKind.Action, A, 10, 1)],
            [PublishedEntryFixtures.Action(A, PublishedAt, 10)],
            await publications.PrepareCorrectionHistoriesAsync([new(firstRecord, [])], default));
        await ExecuteAsync(PublishedEntryFixtures.InsertActionRevisionSql(A, 11));
        await ExecuteAsync("UPDATE action_drafts SET current_revision_number = 11, review_state_version = 2 WHERE record_id = @id", ("id", A));
        var secondRecord = Binding(A, 11);
        await CommitAsync(2, 1, [secondRecord], [new(PublishedRecordKind.Action, A, 11, 2)],
            [PublishedEntryFixtures.Action(A, PublishedAt, 11)],
            await publications.PrepareCorrectionHistoriesAsync([new(secondRecord, [])], default));
        await ExecuteAsync("DELETE FROM release_correction_histories WHERE kind = 'action' AND record_id = @id", ("id", A));

        var page = await publications.ReadCorrectionHistoryBootstrapPageAsync(null, 10, _ => Task.FromResult<string?>(null), default);

        Assert.Equal([10, 11], page.Candidates.Select(candidate => candidate.Record.RevisionNumber).ToArray());
        Assert.All(page.Candidates, candidate => Assert.Equal(PublishedAt, candidate.Record.FirstPublishedAtUtc));
        Assert.All(page.Candidates, candidate => Assert.NotNull(candidate.Record.Facts));
    }

    [Fact]
    public async Task BootstrapKeepsActiveAndServedExceptionsAndRechecksThemBeforeBinding()
    {
        await SeedActionsAsync((A, 10));
        var record = Binding(A, 10);
        var binding = Assert.Single(await publications.PrepareCorrectionHistoriesAsync([new(record, [])], default));
        await CommitAsync(1, null, [record], [new(PublishedRecordKind.Action, A, 10, 1)],
            [PublishedEntryFixtures.Action(A, PublishedAt, 10)], [binding]);
        await ExecuteAsync("""
            UPDATE publication_releases SET published_at_utc_ticks = @old_time WHERE release_number = 1;
            INSERT INTO publication_releases (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
            SELECT n, lpad(n::text, 6, '0') || '-' || md5(n::text), 'test', @old_time + n, 1, false
              FROM generate_series(2, 40) n;
            DELETE FROM release_correction_histories WHERE release_number = 1;
            """, ("old_time", DateTimeOffset.UtcNow.AddDays(-40).UtcTicks));
        var releases = await publications.ListAllAsync(default);
        var active = releases.Single(release => release.ReleaseNumber == 1);
        var served = releases.Single(release => release.ReleaseNumber == 2);
        var page = await publications.ReadCorrectionHistoryBootstrapPageAsync(null, 100,
            _ => Task.FromResult<string?>(served.DirectoryName), default);

        Assert.Equal(new[] { 1, 2 }.Concat(Enumerable.Range(11, 30)), page.Candidates.Select(candidate => candidate.Release.ReleaseNumber));
        Assert.Equal((1, 0), await publications.CommitCorrectionHistoryBootstrapAsync(active, [binding],
            _ => Task.FromResult<string?>(served.DirectoryName), default));
        var newServed = releases.Single(release => release.ReleaseNumber == 40);

        await Assert.ThrowsAsync<InvalidDataException>(() => publications.CommitCorrectionHistoryBootstrapAsync(served, [binding],
            _ => Task.FromResult<string?>(newServed.DirectoryName), default));

        Assert.Equal(0L, await ReadInt64Async("SELECT count(*) FROM release_correction_histories WHERE release_number = 2"));
        var current = await publications.ReadCorrectionHistoryBootstrapPageAsync(null, 100,
            _ => Task.FromResult<string?>(newServed.DirectoryName), default);
        Assert.Equal(Enumerable.Range(11, 30), current.Candidates.Select(candidate => candidate.Release.ReleaseNumber));
    }

    [Fact]
    public async Task BootstrapRefusesARecordWhoseReleaseMembershipChangedAfterPreparation()
    {
        await SeedActionsAsync((A, 10));
        var record = Binding(A, 10);
        var summary = await CommitAsync(1, null, [record], [new(PublishedRecordKind.Action, A, 10, 1)],
            [PublishedEntryFixtures.Action(A, PublishedAt, 10)],
            await publications.PrepareCorrectionHistoriesAsync([new(record, [])], default));
        await CommitAsync(2, 1, [record], [], [], await publications.PrepareCorrectionHistoriesAsync([new(record, [])], default));
        await ExecuteAsync("DELETE FROM release_correction_histories WHERE release_number = 1 AND kind = 'action' AND record_id = @record_id",
            ("record_id", A));
        var history = Assert.Single(await publications.PrepareCorrectionHistoriesAsync([new(record, [])], default));
        await ExecuteAsync("UPDATE publication_record_states SET first_release = 2 WHERE kind = 'action' AND record_id = @record_id",
            ("record_id", A));

        await Assert.ThrowsAsync<InvalidDataException>(() => publications.CommitCorrectionHistoryBootstrapAsync(summary, [history],
            _ => Task.FromResult<string?>(null), default));

        Assert.Equal(0L, await ReadInt64Async("SELECT count(*) FROM release_correction_histories WHERE release_number = 1"));
    }

    [Fact]
    public async Task ReadRefusesWrongRevisionMalformedCountAndChangedBody()
    {
        await SeedActionsAsync((A, 10));
        var record = Binding(A, 10);
        var history = new PublicationCorrectionHistory(record, [Correction(9, PublishedAt, "Verified correction")]);
        var binding = Assert.Single(await publications.PrepareCorrectionHistoriesAsync([history], default));
        await CommitAsync(1, null, [record], [new(PublishedRecordKind.Action, A, 10, 1)],
            [PublishedEntryFixtures.Action(A, PublishedAt, 10)], [binding]);

        await Assert.ThrowsAsync<InvalidDataException>(() => publications.ReadCorrectionHistoriesAsync(1, [Binding(A, 9)], default));
        await ExecuteAsync("UPDATE published_correction_histories SET correction_count = 2 WHERE history_id = @history_id",
            ("history_id", binding.HistoryId));
        await Assert.ThrowsAsync<InvalidDataException>(() => publications.ReadCorrectionHistoriesAsync(1, [record], default));

        await ExecuteAsync("UPDATE published_correction_histories SET correction_count = 1 WHERE history_id = @history_id",
            ("history_id", binding.HistoryId));
        await ExecuteAsync("UPDATE published_correction_history_entries SET note = 'Tampered correction' WHERE history_id = @history_id AND position = 0",
            ("history_id", binding.HistoryId));
        await Assert.ThrowsAsync<InvalidDataException>(() => publications.ReadCorrectionHistoriesAsync(1, [record], default));
    }

    [Fact]
    public async Task CommitRejectsAHistoryBoundToAnIncompatibleRevisionAndRollsBackItsWrites()
    {
        await SeedActionsAsync((A, 2));
        var prepared = Assert.Single(await publications.PrepareCorrectionHistoriesAsync(
            [new(Binding(A, 10), [Correction(9, PublishedAt, "For revision ten")])], default));
        var target = Binding(A, 2);
        var forged = new CorrectionHistoryBinding(target, prepared.HistoryId);

        await Assert.ThrowsAsync<InvalidDataException>(() => CommitAsync(1, null, [target],
            [new(PublishedRecordKind.Action, A, 2, 1)], [PublishedEntryFixtures.Action(A, PublishedAt, 2)], [forged]));

        Assert.Equal(new PublicationState(0, null), await publications.GetStateAsync(default));
        Assert.Equal(0L, await ReadInt64Async("SELECT count(*) FROM publication_releases"));
        Assert.Equal(0L, await ReadInt64Async("SELECT count(*) FROM release_correction_histories"));
        Assert.Equal(0L, await ReadInt64Async("SELECT count(*) FROM publication_record_states WHERE kind = 'action' AND record_id = @record_id",
            ("record_id", A)));
        Assert.Equal(0L, await ReadInt64Async("SELECT count(*) FROM published_entries WHERE kind = 'action' AND record_id = @record_id AND revision_number = 2",
            ("record_id", A)));
        Assert.Equal(0L, await ReadInt64Async(
            "SELECT count(*) FROM document_change_review_idempotency WHERE operation = 'publish-release'"));
    }

    private async Task<PublicationReleaseSummary> CommitAsync(int number, int? baseNumber, IEnumerable<PublishedRevisionBinding> records,
        IEnumerable<ReviewStateExpectation> expectedStates, IEnumerable<PublishedRecordEntry> addedEntries,
        IEnumerable<CorrectionHistoryBinding> histories)
    {
        var commit = new PublicationCommit(number, baseNumber, $"{number:D6}-{Guid.NewGuid():N}", [.. records], [.. expectedStates])
        {
            Withdrawals = [],
            AddedEntries = [.. addedEntries],
            CorrectionHistories = [.. histories],
            Names = ReleaseNameFixtures.None
        };
        commitCount++;
        return await publications.CommitAsync("auth0|owner", commit, $"key-{commitCount}",
            Convert.ToHexStringLower(BitConverter.GetBytes(commitCount)).PadLeft(64, '0'), default);
    }

    private async Task SeedActionsAsync(params (string Id, int Revision)[] actions)
    {
        var billNumber = 0;
        foreach (var (id, revision) in actions)
        {
            billNumber++;
            await ExecuteAsync(PublishedEntryFixtures.InsertActionRevisionSql(id, revision));
            await ExecuteAsync($"""
                SET session_replication_role = replica;
                INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number,
                    created_at_utc_ticks, last_revised_at_utc_ticks, review_state_version, review_status)
                VALUES ('{id}', 'congress-bills', 119, 'hr', {billNumber}, {revision}, 0, 0, 1, 'Approved')
                """);
        }
    }

    private async Task ExecuteAsync(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<long> ReadInt64Async(string sql, params (string Name, object Value)[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return Convert.ToInt64(await command.ExecuteScalarAsync());
    }

    private async Task<List<CorrectionRow>> ReadCorrectionRowsAsync(string historyId)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT position, replaced_revision_number, published_at_utc_ticks, published_offset_minutes, note, facts_unchanged
              FROM published_correction_history_entries WHERE history_id = @history_id ORDER BY position
            """, connection);
        command.Parameters.AddWithValue("history_id", historyId);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<CorrectionRow>();
        while (await reader.ReadAsync())
            rows.Add(new(reader.GetInt16(0), reader.GetInt32(1), reader.GetInt64(2), reader.GetInt16(3),
                reader.IsDBNull(4) ? null : reader.GetString(4), reader.GetBoolean(5)));
        return rows;
    }

    private async Task<int[]> ReadOffsetsAsync(string[] historyIds)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SELECT published_offset_minutes FROM published_correction_history_entries
             WHERE history_id = ANY(@history_ids) ORDER BY published_offset_minutes
            """, connection);
        command.Parameters.AddWithValue("history_ids", historyIds);
        await using var reader = await command.ExecuteReaderAsync();
        var offsets = new List<int>();
        while (await reader.ReadAsync()) offsets.Add(reader.GetInt16(0));
        return [.. offsets];
    }

    private static PublishedRevisionBinding Binding(string recordId, int revision) => new(PublishedRecordKind.Action, recordId, revision);

    private static PublishedCorrection[] OrderedCorrections() =>
    [
        Correction(9, PublishedAt.ToOffset(TimeSpan.FromMinutes(345)), null, factsUnchanged: true),
        Correction(8, PublishedAt.AddMinutes(-1), "Corrected caf\u00e9 quote\u001f\nSecond line", factsUnchanged: false),
        Correction(7, PublishedAt.AddMinutes(-2), null, factsUnchanged: false)
    ];

    private static PublishedCorrection Correction(int replacedRevision, DateTimeOffset at, string? note, bool factsUnchanged = false) => new()
    {
        ReplacedRevisionNumber = replacedRevision,
        PublishedAtUtc = at,
        Note = note,
        FactsUnchanged = factsUnchanged
    };

    private static void AssertCorrection(CorrectionRow row, int position, PublishedCorrection expected)
    {
        Assert.Equal(position, row.Position);
        Assert.Equal(expected.ReplacedRevisionNumber, row.ReplacedRevisionNumber);
        Assert.Equal(expected.PublishedAtUtc.UtcTicks, row.UtcTicks);
        Assert.Equal((short)expected.PublishedAtUtc.Offset.TotalMinutes, row.OffsetMinutes);
        Assert.Equal(expected.Note, row.Note);
        Assert.Equal(expected.FactsUnchanged, row.FactsUnchanged);
    }

    private sealed record CorrectionRow(short Position, int ReplacedRevisionNumber, long UtcTicks, short OffsetMinutes,
        string? Note, bool FactsUnchanged);
}
