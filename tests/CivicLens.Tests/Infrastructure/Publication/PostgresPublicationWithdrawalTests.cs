using System.Text.Json;
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
public sealed class PostgresPublicationWithdrawalTests(PostgresCollection postgres) : IAsyncLifetime
{
    private static readonly string A = new('a', 64);
    private static readonly string B = new('b', 64);
    private static readonly string C = new('c', 64);
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
    public async Task TheRowsOfTheActiveReleaseRebuildTheManifestWithdrawalsWithTheirNotesAndCorrectionOrder()
    {
        await SeedActionsAsync(A, B, C);
        await PublishAsync(1, null, [A, B, C], added: [A, B, C]);
        var b = Withdrawal(B, "Wrong bill.", First.AddDays(2), Correction(2, "Fixed.", false), Correction(1, null, true));
        var a = Withdrawal(A, "Wrong date.", First.AddDays(3));
        await SetStatusAsync(B, "ApprovalWithdrawn");
        await SetStatusAsync(A, "ApprovalWithdrawn");

        await PublishAsync(2, 1, [C], withdrawn: [b, a]);

        Assert.Equal(Json([a, b]), Json(await ReadWithdrawalsAsync(2)));
    }

    [Fact]
    public async Task TheStoreReadsTwoWithdrawalsOfOneKindInOneReleaseBecauseVerifyMustReachEveryLaterRelease()
    {
        await SeedActionsAsync(A, B, C);
        await PublishAsync(1, null, [A, B, C], added: [A, B, C]);
        var b = Withdrawal(B, "Wrong bill.", First.AddDays(2), Correction(1, "Fixed.", false));
        var a = Withdrawal(A, "Wrong date.", First.AddDays(3));
        await SetStatusAsync(B, "ApprovalWithdrawn");
        await SetStatusAsync(A, "ApprovalWithdrawn");
        await PublishAsync(2, 1, [C], withdrawn: [b, a]);

        var stored = await publications.ReadReleaseAsync(2, default);

        Assert.Equal(Json([a, b]), Json(stored.Withdrawals.OrderBy(withdrawal => withdrawal.RecordId)));
    }

    [Fact]
    public async Task ACarriedWithdrawalKeepsItsOneRowAndALaterReleaseAddsNoRow()
    {
        await SeedActionsAsync(A, B);
        await PublishAsync(1, null, [A, B], added: [A, B]);
        await SetStatusAsync(A, "ApprovalWithdrawn");
        var withdrawal = Withdrawal(A, "Wrong.", First.AddDays(1));
        await PublishAsync(2, 1, [B], withdrawn: [withdrawal]);

        await PublishAsync(3, 2, [B], carried: [withdrawal]);

        Assert.Equal(["2|NULL"], await RangesAsync(A));
        Assert.Equal(Json([withdrawal]), Json(await ReadWithdrawalsAsync(3)));
    }

    [Fact]
    public async Task AReturnClosesTheRangeAndASecondWithdrawalOpensANewRow()
    {
        await SeedActionsAsync(A, B);
        await PublishAsync(1, null, [A, B], added: [A, B]);
        await SetStatusAsync(A, "ApprovalWithdrawn");
        var first = Withdrawal(A, "Wrong.", First.AddDays(1), Correction(1, "Early.", false));
        await PublishAsync(2, 1, [B], withdrawn: [first]);
        await SetStatusAsync(A, "Approved");
        await PublishAsync(3, 2, [A, B], added: [A]);
        await SetStatusAsync(A, "ApprovalWithdrawn");
        var second = Withdrawal(A, "Wrong again.", First.AddDays(5));

        await PublishAsync(4, 3, [B], withdrawn: [second]);

        Assert.Equal(["2|2", "4|NULL"], await RangesAsync(A));
        Assert.Equal(Json([first]), Json(await ReadWithdrawalsAsync(2)));
        Assert.Empty(await ReadWithdrawalsAsync(3));
        Assert.Equal(Json([second]), Json(await ReadWithdrawalsAsync(4)));
    }

    // Release 3 withdraws A, release 2 comes back, and release 4 keeps A. A diff against the active release would leave the row of release 3 open.
    [Fact]
    public async Task AWithdrawalOfARolledBackReleaseClosesAtTheHighestReleaseSoItNeverCoversTheNextRelease()
    {
        await SeedActionsAsync(A, B);
        await PublishAsync(1, null, [A, B], added: [A, B]);
        await PublishAsync(2, 1, [A, B]);
        await SetStatusAsync(A, "ApprovalWithdrawn");
        await PublishAsync(3, 2, [B], withdrawn: [Withdrawal(A, "Wrong.", First.AddDays(1))]);
        await publications.ActivateAsync(2, (_, _) => Task.CompletedTask, default);
        await SetStatusAsync(A, "Approved");

        await PublishAsync(4, 2, [A, B]);

        Assert.Equal(["3|3"], await RangesAsync(A));
        Assert.Empty(await ReadWithdrawalsAsync(4));
    }

    [Fact]
    public async Task AWithdrawalOfARolledBackReleaseOpensANewRowWhenALaterReleaseCarriesItAgain()
    {
        await SeedActionsAsync(A, B);
        await PublishAsync(1, null, [A, B], added: [A, B]);
        await SetStatusAsync(A, "ApprovalWithdrawn");
        var withdrawal = Withdrawal(A, "Wrong.", First.AddDays(1));
        await PublishAsync(2, 1, [B], withdrawn: [withdrawal]);
        await SetStatusAsync(A, "Approved");
        await PublishAsync(3, 2, [A, B], added: [A]);
        await publications.ActivateAsync(2, (_, _) => Task.CompletedTask, default);

        await PublishAsync(4, 2, [B], carried: [withdrawal]);

        Assert.Equal(["2|2", "4|NULL"], await RangesAsync(A));
        Assert.Equal(Json([withdrawal]), Json(await ReadWithdrawalsAsync(4)));
    }

    [Fact]
    public async Task AReplayReturnsTheStoredSummaryAndWritesNoRow()
    {
        await SeedActionsAsync(A, B);
        await PublishAsync(1, null, [A, B], added: [A, B]);
        await SetStatusAsync(A, "ApprovalWithdrawn");
        var commit = Commit(2, 1, [B], withdrawn: [Withdrawal(A, "Wrong.", First.AddDays(1), Correction(1, "Early.", false))]);

        await publications.CommitAsync("auth0|owner", commit, "replay", new string('1', 64), default);
        await publications.CommitAsync("auth0|owner", commit, "replay", new string('1', 64), default);

        Assert.Equal(["1"], (await ReadAsync("SELECT count(*) FROM publication_withdrawals")).Select(row => row[0]));
        Assert.Equal(["1"], (await ReadAsync("SELECT count(*) FROM publication_withdrawal_corrections")).Select(row => row[0]));
    }

    [Theory]
    [InlineData("ck_publication_withdrawals_range", "'action', 'KEY', 1, 5, 'Wrong.', 2, 1")]
    [InlineData("ck_publication_withdrawals_revision", "'action', 'KEY', 0, 5, 'Wrong.', 1, NULL")]
    [InlineData("ck_publication_withdrawals_note", "'action', 'KEY', 1, 5, '', 1, NULL")]
    [InlineData("ck_publication_withdrawals_note", "'action', 'KEY', 1, 5, '   ', 1, NULL")]
    [InlineData("ck_publication_withdrawals_withdrawn_at_utc_ticks", "'action', 'KEY', 1, -1, 'Wrong.', 1, NULL")]
    public async Task TheWithdrawalTableRefusesEachBrokenRule(string check, string values)
    {
        await SeedReleaseWithRecordAsync();

        var failure = await Assert.ThrowsAsync<PostgresException>(() => InsertWithdrawalAsync(values.Replace("KEY", A)));

        Assert.Equal((PostgresErrorCodes.CheckViolation, check), (failure.SqlState, failure.ConstraintName));
    }

    [Fact]
    public async Task TheWithdrawalNoteLimitIsTheContractLimit()
    {
        await SeedReleaseWithRecordAsync();

        await InsertWithdrawalAsync($"'action', '{A}', 1, 5, '{new string('x', PublishedCorrection.MaximumNoteLength)}', 1, NULL");
        var failure = await Assert.ThrowsAsync<PostgresException>(() =>
            InsertWithdrawalAsync($"'action', '{B}', 1, 5, '{new string('x', PublishedCorrection.MaximumNoteLength + 1)}', 1, NULL"));

        Assert.Equal("ck_publication_withdrawals_note", failure.ConstraintName);
    }

    [Fact]
    public async Task TheWithdrawalTableRefusesARecordThatWasNeverPublishedAndAnUnknownRelease()
    {
        await SeedReleaseWithRecordAsync();

        var unpublished = await Assert.ThrowsAsync<PostgresException>(() => InsertWithdrawalAsync($"'action', '{B}', 1, 5, 'Wrong.', 1, NULL"));
        var noRelease = await Assert.ThrowsAsync<PostgresException>(() => InsertWithdrawalAsync($"'action', '{A}', 1, 5, 'Wrong.', 9, NULL"));

        Assert.Equal([PostgresErrorCodes.ForeignKeyViolation, PostgresErrorCodes.ForeignKeyViolation], [unpublished.SqlState, noRelease.SqlState]);
        Assert.Equal(["FK_publication_withdrawals_published_records_kind_record_id", "FK_publication_withdrawals_publication_releases_first_release"],
            [unpublished.ConstraintName!, noRelease.ConstraintName!]);
    }

    [Theory]
    [InlineData("ck_publication_withdrawal_corrections_position", "-1, 1, 5, NULL, false")]
    [InlineData("ck_publication_withdrawal_corrections_revision", "0, 0, 5, NULL, false")]
    [InlineData("ck_publication_withdrawal_corrections_note", "0, 1, 5, '', false")]
    [InlineData("ck_publication_withdrawal_corrections_note", "0, 1, 5, '   ', false")]
    [InlineData("ck_publication_withdrawal_corrections_facts_unchanged", "0, 1, 5, 'Fixed.', true")]
    [InlineData("ck_publication_withdrawal_corrections_published_at_utc_ticks", "0, 1, -1, NULL, false")]
    public async Task TheCorrectionTableRefusesEachBrokenRule(string check, string values)
    {
        await SeedReleaseWithRecordAsync();
        await InsertWithdrawalAsync($"'action', '{A}', 3, 5, 'Wrong.', 1, NULL");

        var failure = await Assert.ThrowsAsync<PostgresException>(() => InsertCorrectionAsync(A, values));

        Assert.Equal((PostgresErrorCodes.CheckViolation, check), (failure.SqlState, failure.ConstraintName));
    }

    [Fact]
    public async Task TheCorrectionTableRefusesAWithdrawalThatDoesNotExistAndADuplicatePosition()
    {
        await SeedReleaseWithRecordAsync();
        await InsertWithdrawalAsync($"'action', '{A}', 3, 5, 'Wrong.', 1, NULL");
        await InsertCorrectionAsync(A, "0, 1, 5, NULL, false");

        var duplicate = await Assert.ThrowsAsync<PostgresException>(() => InsertCorrectionAsync(A, "0, 2, 5, NULL, false"));
        var orphan = await Assert.ThrowsAsync<PostgresException>(() => InsertCorrectionAsync(B, "0, 1, 5, NULL, false"));

        Assert.Equal([PostgresErrorCodes.UniqueViolation, PostgresErrorCodes.ForeignKeyViolation], [duplicate.SqlState, orphan.SqlState]);
    }

    [Fact]
    public async Task TheMigrationCreatesBothTablesAndGoesDownByDroppingThemBecauseDownMustWorkForARollback()
    {
        await using var db = new CollectionAttemptDbContext(new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options);
        var migrations = db.Database.GetMigrations().ToList();
        var migration = migrations.Single(id => id.EndsWith("_PublicationWithdrawals", StringComparison.Ordinal));
        Assert.Equal(2, (await TablesAsync()).Count);

        await db.GetService<IMigrator>().MigrateAsync(migrations[migrations.IndexOf(migration) - 1]);

        Assert.Empty(await TablesAsync());
        await db.Database.MigrateAsync();
        Assert.Equal(2, (await TablesAsync()).Count);
    }

    private Task<List<string[]>> TablesAsync() => ReadAsync(
        "SELECT table_name FROM information_schema.tables WHERE table_name IN ('publication_withdrawals', 'publication_withdrawal_corrections')");

    private async Task SeedReleaseWithRecordAsync()
    {
        await SeedActionsAsync(A);
        await PublishAsync(1, null, [A], added: [A]);
    }

    private Task InsertWithdrawalAsync(string values) => ExecuteAsync($"""
        INSERT INTO publication_withdrawals (kind, record_id, withdrawn_revision_number, withdrawn_at_utc_ticks, note, first_release, last_release)
        VALUES ({values})
        """);

    private Task InsertCorrectionAsync(string recordId, string values) => ExecuteAsync($"""
        INSERT INTO publication_withdrawal_corrections (kind, record_id, first_release, position, replaced_revision_number, published_at_utc_ticks, note, facts_unchanged)
        VALUES ('action', '{recordId}', 1, {values})
        """);

    private async Task<PublishedWithdrawal[]> ReadWithdrawalsAsync(int release)
    {
        var rows = await ReadAsync($"""
            SELECT record_id, first_release, withdrawn_revision_number, withdrawn_at_utc_ticks, note FROM publication_withdrawals
            WHERE first_release <= {release} AND (last_release IS NULL OR last_release >= {release})
            """);
        var withdrawals = new List<PublishedWithdrawal>();
        foreach (var row in rows)
        {
            var corrections = await ReadAsync($"""
                SELECT replaced_revision_number, published_at_utc_ticks, COALESCE(note, 'NULL'), facts_unchanged FROM publication_withdrawal_corrections
                WHERE record_id = '{row[0]}' AND first_release = {row[1]} ORDER BY position
                """);
            withdrawals.Add(new()
            {
                Kind = PublishedRecordKind.Action,
                RecordId = row[0],
                WithdrawnRevisionNumber = int.Parse(row[2]),
                FirstPublishedAtUtc = First,
                WithdrawnAtUtc = new DateTimeOffset(long.Parse(row[3]), TimeSpan.Zero),
                Note = row[4],
                Corrections = [.. corrections.Select(correction => new PublishedCorrection
                {
                    ReplacedRevisionNumber = int.Parse(correction[0]),
                    PublishedAtUtc = new DateTimeOffset(long.Parse(correction[1]), TimeSpan.Zero),
                    Note = correction[2] == "NULL" ? null : correction[2],
                    FactsUnchanged = bool.Parse(correction[3])
                })]
            });
        }
        return [.. withdrawals.OrderByDescending(item => item.WithdrawnAtUtc).ThenBy(item => item.RecordId, StringComparer.Ordinal)];
    }

    private async Task<List<string>> RangesAsync(string recordId) =>
        [.. (await ReadAsync($"""
            SELECT first_release || '|' || COALESCE(last_release::text, 'NULL') FROM publication_withdrawals
            WHERE record_id = '{recordId}' ORDER BY first_release
            """)).Select(row => row[0])];

    private static string Json(IEnumerable<PublishedWithdrawal> withdrawals) =>
        JsonSerializer.Serialize(withdrawals, PublicationProtocol.JsonOptions);

    private static PublishedWithdrawal Withdrawal(string id, string note, DateTimeOffset at, params PublishedCorrection[] corrections) => new()
    {
        Kind = PublishedRecordKind.Action,
        RecordId = id,
        WithdrawnRevisionNumber = 3,
        FirstPublishedAtUtc = First,
        WithdrawnAtUtc = at,
        Note = note,
        Corrections = corrections
    };

    private static PublishedCorrection Correction(int replaced, string? note, bool factsUnchanged) => new()
    {
        ReplacedRevisionNumber = replaced,
        PublishedAtUtc = First.AddHours(replaced),
        Note = note,
        FactsUnchanged = factsUnchanged
    };

    private Task PublishAsync(int number, int? baseNumber, string[] records, string[]? added = null,
        PublishedWithdrawal[]? withdrawn = null, PublishedWithdrawal[]? carried = null) =>
        publications.CommitAsync("auth0|owner", Commit(number, baseNumber, records, added, withdrawn, carried),
            $"key-{++commitCount}", Convert.ToHexStringLower(BitConverter.GetBytes(commitCount)).PadLeft(64, '0'), default);

    private static PublicationCommit Commit(int number, int? baseNumber, string[] records, string[]? added = null,
        PublishedWithdrawal[]? withdrawn = null, PublishedWithdrawal[]? carried = null) =>
        new(number, baseNumber, $"{number:D6}-{Guid.NewGuid():N}",
            [.. records.Select(id => new PublishedRevisionBinding(PublishedRecordKind.Action, id, 1))],
            [.. (added ?? []).Select(id => new ReviewStateExpectation(PublishedRecordKind.Action, id, 1, 1))])
        {
            WithdrawnRecords = [.. (withdrawn ?? []).Select(item => new PublishedRecordReference(item.Kind, item.RecordId))],
            Withdrawals = [.. (carried ?? []), .. withdrawn ?? []],
            AddedEntries = [.. (added ?? []).Select(id => PublishedEntryFixtures.Action(id, First))]
        };

    private async Task SeedActionsAsync(params string[] ids)
    {
        foreach (var (id, index) in ids.Select((id, index) => (id, index)))
        {
            await ExecuteAsync(PublishedEntryFixtures.InsertActionRevisionSql(id));
            await ExecuteAsync($"""
                SET session_replication_role = replica;
                INSERT INTO action_drafts (record_id, source_id, congress, bill_type, bill_number, current_revision_number, created_at_utc_ticks,
                    last_revised_at_utc_ticks, review_state_version, review_status)
                VALUES ('{id}', 'congress-bills', 119, 'hr', {index + 1}, 1, 0, 0, 1, 'Approved')
                """);
        }
    }

    private Task SetStatusAsync(string id, string status) =>
        ExecuteAsync($"UPDATE action_drafts SET review_status = '{status}' WHERE record_id = '{id}'");

    private async Task ExecuteAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync();
    }

    private Task<List<string[]>> ReadAsync(string sql) => PublishedRecordStateReader.ReadAsync(connectionString, sql);
}
