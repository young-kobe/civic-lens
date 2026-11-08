using CivicLens.Infrastructure.Collection;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql;

namespace CivicLens.Tests.Infrastructure.Collection.BillStream;

public sealed partial class PostgresBillStreamTests
{
    private const string HistoryProjectionMigration = "20261108053100_BillHistoryProjectionVersion";
    private const string BillHistoryImportsMigration = "20261108053000_BillHistoryImports";

    [Fact]
    public async Task ProjectionVersionMigrationPreservesLegacyRowsAndPublicViewShapesAndDowngradesVersionZero()
    {
        var viewsBefore = await BillHistoryViewShapeAsync();
        await AssertPublicBillViewsReadableAsync();
        await MigrateToAsync(BillHistoryImportsMigration);
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        var detailJobId = (string)(await ScalarAsync("SELECT detail_job_id FROM bill_gates"))!;
        await SeedReleaseAsync(1, active: true);
        await SeedLegacyBillHistoryAsync(detailJobId);
        var legacyRows = await BillHistoryRowsJsonAsync();

        await MigrateToAsync(null);

        Assert.Equal(viewsBefore, await BillHistoryViewShapeAsync());
        await AssertPublicBillViewsReadableAsync();
        Assert.Equal(legacyRows, await BillHistoryRowsJsonAsync());
        Assert.Equal("0|0|0", await ScalarWithParametersAsync("""
            SELECT (SELECT string_agg(projection_version::text, ',' ORDER BY projection_version) FROM published_bill_histories)
                 || '|' || (SELECT string_agg(projection_version::text, ',' ORDER BY projection_version) FROM published_bill_history_actions)
                 || '|' || (SELECT string_agg(projection_version::text, ',' ORDER BY projection_version) FROM release_bill_histories)
            """));

        await MigrateToAsync(BillHistoryImportsMigration);
        Assert.Equal(legacyRows, await BillHistoryRowsJsonAsync());
    }

    [Fact]
    public async Task ProjectionVersionsAllowCoexistingCopiesAndBlockDowngradeWhileVerifiedRowsExist()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        var detailJobId = (string)(await ScalarAsync("SELECT detail_job_id FROM bill_gates"))!;
        await SeedReleaseAsync(1, active: false);
        await SeedReleaseAsync(2, active: true);
        await SeedReleaseAsync(3, active: false);
        await SeedVersionedBillHistoryAsync(detailJobId);

        var incorrectVersion = await Assert.ThrowsAsync<PostgresException>(() => ExecuteWithParametersAsync("""
            INSERT INTO release_bill_histories (release_number, record_id, detail_job_id, projection_version)
            VALUES (3, @record_id, @detail_job_id, 2)
            """, new NpgsqlParameter("record_id", RecordId), new NpgsqlParameter("detail_job_id", detailJobId)));
        Assert.Equal("23503", incorrectVersion.SqlState);

        Assert.Equal(new[] { "1|Legacy action", "2|Verified action" }, await ReadStringsAsync("""
            SELECT rh.release_number::text || '|' || a.text
              FROM release_bill_histories rh
              JOIN published_bill_history_actions a
                ON a.detail_job_id = rh.detail_job_id AND a.projection_version = rh.projection_version
             ORDER BY rh.release_number
            """));
        var downgrade = await Assert.ThrowsAsync<PostgresException>(() => MigrateToAsync(BillHistoryImportsMigration));
        Assert.Equal("P0001", downgrade.SqlState);
        Assert.Equal("0,1|0,1|0,1", await ScalarAsync("""
            SELECT (SELECT string_agg(projection_version::text, ',' ORDER BY projection_version) FROM published_bill_histories)
                 || '|' || (SELECT string_agg(projection_version::text, ',' ORDER BY projection_version) FROM published_bill_history_actions)
                 || '|' || (SELECT string_agg(projection_version::text, ',' ORDER BY projection_version) FROM release_bill_histories)
            """));
        Assert.Equal("2", await ScalarAsync("SELECT count(*)::text FROM published_bill_histories"));
        Assert.Equal("2", await ScalarAsync("SELECT count(*)::text FROM published_bill_history_actions"));
        Assert.Equal("2", await ScalarAsync("SELECT count(*)::text FROM release_bill_histories"));
        Assert.Equal("PRIMARY KEY (detail_job_id, projection_version)", await ScalarAsync("""
            SELECT pg_get_constraintdef(oid)
              FROM pg_constraint
             WHERE conrelid = 'published_bill_histories'::regclass AND contype = 'p'
            """));
        Assert.Equal("PRIMARY KEY (detail_job_id, projection_version, position)", await ScalarAsync("""
            SELECT pg_get_constraintdef(oid)
              FROM pg_constraint
             WHERE conrelid = 'published_bill_history_actions'::regclass AND contype = 'p'
            """));
        Assert.Equal("PRIMARY KEY (release_number, record_id)", await ScalarAsync("""
            SELECT pg_get_constraintdef(oid)
              FROM pg_constraint
             WHERE conrelid = 'release_bill_histories'::regclass AND contype = 'p'
            """));
    }

    [Fact]
    public async Task DowngradeCannotEraseAProjectionCommittedWhileItIsStarting()
    {
        await DraftForReviewAsync(UpdatedAt, DraftableOutput);
        var detailJobId = (string)(await ScalarAsync("SELECT detail_job_id FROM bill_gates"))!;
        await MigrateToAsync(HistoryProjectionMigration);

        await using var writer = new NpgsqlConnection(connectionString);
        await writer.OpenAsync();
        await using var writerTransaction = await writer.BeginTransactionAsync();
        await using (var childLock = new NpgsqlCommand(
            "LOCK TABLE published_bill_history_actions IN ACCESS EXCLUSIVE MODE", writer, writerTransaction))
            await childLock.ExecuteNonQueryAsync();
        await using (var insert = new NpgsqlCommand("""
            INSERT INTO published_bill_histories
                (detail_job_id, projection_version, source_id, congress, bill_type, bill_number,
                 observed_at_utc_ticks, action_count, public_law)
            SELECT detail_job_id, 1, source_id, congress, bill_type, bill_number,
                   638713728000000000, 0, NULL
              FROM bill_gates
             WHERE detail_job_id = @detail_job_id
            """, writer, writerTransaction))
        {
            insert.Parameters.AddWithValue("detail_job_id", detailJobId);
            await insert.ExecuteNonQueryAsync();
        }

        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        await using var migrationConnection = new NpgsqlConnection(connectionString);
        await migrationConnection.OpenAsync(timeout.Token);
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>()
            .UseNpgsql(migrationConnection)
            .Options;
        await using var db = new CollectionAttemptDbContext(options);
        var migration = db.GetService<IMigrator>().MigrateAsync(BillHistoryImportsMigration, timeout.Token);
        var writerCommitted = false;

        try
        {
            await WaitForDowngradeBlockOrCompletionAsync(migration, migrationConnection.ProcessID, writer.ProcessID, timeout.Token);

            await writerTransaction.CommitAsync(timeout.Token);
            writerCommitted = true;

            var failure = await Assert.ThrowsAsync<PostgresException>(async () => await migration);
            Assert.Equal("P0001", failure.SqlState);
            Assert.Equal("1", await ScalarWithParametersAsync("""
                SELECT projection_version::text
                  FROM published_bill_histories
                 WHERE detail_job_id = @detail_job_id
                """, new NpgsqlParameter("detail_job_id", detailJobId)));
            Assert.Equal("1", await ScalarAsync("""
                SELECT count(*)::text
                  FROM information_schema.columns
                 WHERE table_schema = 'public' AND table_name = 'published_bill_histories'
                   AND column_name = 'projection_version'
                """));
        }
        finally
        {
            if (!writerCommitted)
                await writerTransaction.RollbackAsync(CancellationToken.None);
            if (!migration.IsCompleted)
                timeout.Cancel();
            try { await migration; }
            catch (Exception) { }
        }
    }

    private async Task WaitForDowngradeBlockOrCompletionAsync(
        Task migration,
        int migrationBackendPid,
        int writerBackendPid,
        CancellationToken cancellationToken)
    {
        await using var observer = new NpgsqlConnection(connectionString);
        await observer.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand("""
            SELECT @writer_pid = ANY(pg_blocking_pids(@migration_pid))
            """, observer);
        command.Parameters.AddWithValue("writer_pid", writerBackendPid);
        command.Parameters.AddWithValue("migration_pid", migrationBackendPid);
        while (!migration.IsCompleted)
        {
            if ((bool)(await command.ExecuteScalarAsync(cancellationToken))!)
                return;
            await Task.Delay(TimeSpan.FromMilliseconds(20), cancellationToken);
        }
    }

    private async Task MigrateToAsync(string? target)
    {
        if (target is null)
        {
            await PostgresCollectionAttemptStore.FromConnectionString(connectionString).MigrateAsync();
            return;
        }
        var options = new DbContextOptionsBuilder<CollectionAttemptDbContext>().UseNpgsql(connectionString).Options;
        await using var db = new CollectionAttemptDbContext(options);
        await db.GetService<IMigrator>().MigrateAsync(target);
    }

    private async Task SeedReleaseAsync(int releaseNumber, bool active)
    {
        await ExecuteWithParametersAsync("""
            INSERT INTO publication_releases
                (release_number, directory_name, actor_subject, published_at_utc_ticks, record_count, is_active)
            VALUES (@release_number, @directory_name, 'history-migration-test', 0, 1, @is_active)
            """,
            new NpgsqlParameter("release_number", releaseNumber),
            new NpgsqlParameter("directory_name", $"{releaseNumber:D6}-{Guid.NewGuid():N}"),
            new NpgsqlParameter("is_active", active));
    }

    private async Task SeedLegacyBillHistoryAsync(string detailJobId)
    {
        await ExecuteWithParametersAsync("""
        INSERT INTO published_bill_histories
            (detail_job_id, source_id, congress, bill_type, bill_number, observed_at_utc_ticks, action_count, public_law)
        SELECT detail_job_id, source_id, congress, bill_type, bill_number, 638713728000000000, 1, '119-21'
          FROM bill_gates WHERE detail_job_id = @detail_job_id
        """, new NpgsqlParameter("detail_job_id", detailJobId));
        await ExecuteWithParametersAsync("""
        INSERT INTO published_bill_history_actions
            (detail_job_id, position, action_date, action_time, text, type, action_code, source_system_code, source_system_name)
        VALUES (@detail_job_id, 0, DATE '2025-03-02', TIME '13:45:10', 'Legacy action', 'Floor', 'H8D000', 2, 'House floor actions')
        """, new NpgsqlParameter("detail_job_id", detailJobId));
        await ExecuteWithParametersAsync("""
        INSERT INTO release_bill_histories (release_number, record_id, detail_job_id)
        VALUES (1, @record_id, @detail_job_id)
        """, new NpgsqlParameter("detail_job_id", detailJobId), new NpgsqlParameter("record_id", RecordId));
    }

    private async Task SeedVersionedBillHistoryAsync(string detailJobId)
    {
        await ExecuteWithParametersAsync("""
        INSERT INTO published_bill_histories
            (detail_job_id, projection_version, source_id, congress, bill_type, bill_number, observed_at_utc_ticks, action_count, public_law)
        SELECT detail_job_id, versions.projection_version, source_id, congress, bill_type, bill_number,
               638713728000000000, 1, '119-21'
          FROM bill_gates CROSS JOIN (VALUES (0), (1)) AS versions(projection_version)
         WHERE detail_job_id = @detail_job_id
        """, new NpgsqlParameter("detail_job_id", detailJobId));
        await ExecuteWithParametersAsync("""
        INSERT INTO published_bill_history_actions
            (detail_job_id, projection_version, position, action_date, action_time, text, type, action_code, source_system_code, source_system_name)
        VALUES (@detail_job_id, 0, 0, DATE '2025-03-02', TIME '13:45:10', 'Legacy action', 'Floor', 'H8D000', 2, 'House floor actions'),
               (@detail_job_id, 1, 0, DATE '2025-03-02', TIME '13:45:10', 'Verified action', 'Floor', 'H8D000', 2, 'House floor actions')
        """, new NpgsqlParameter("detail_job_id", detailJobId));
        await ExecuteWithParametersAsync("""
        INSERT INTO release_bill_histories (release_number, record_id, detail_job_id, projection_version)
        VALUES (1, @record_id, @detail_job_id, 0), (2, @record_id, @detail_job_id, 1)
        """, new NpgsqlParameter("detail_job_id", detailJobId), new NpgsqlParameter("record_id", RecordId));
    }

    private async Task<string> BillHistoryRowsJsonAsync() => (string)(await ScalarAsync("""
        SELECT jsonb_build_object(
            'histories', (SELECT jsonb_agg(to_jsonb(h) - 'projection_version' ORDER BY detail_job_id) FROM published_bill_histories h),
            'actions', (SELECT jsonb_agg(to_jsonb(a) - 'projection_version' ORDER BY detail_job_id, position) FROM published_bill_history_actions a),
            'release_bindings', (SELECT jsonb_agg(to_jsonb(r) - 'projection_version' ORDER BY release_number, record_id) FROM release_bill_histories r)
        )::text
        """))!;

    private async Task<string[]> BillHistoryViewShapeAsync() => await ReadStringsAsync("""
        SELECT table_name || '|' || ordinal_position::text || '|' || column_name || '|' || data_type || '|' || udt_name
          FROM information_schema.columns
         WHERE table_schema = 'public_api' AND table_name IN ('bills', 'bill_actions')
         ORDER BY table_name, ordinal_position
        """);

    private async Task AssertPublicBillViewsReadableAsync()
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand("""
            SET ROLE civic_lens_public;
            SELECT * FROM public_api.bills LIMIT 0;
            SELECT * FROM public_api.bill_actions LIMIT 0;
            """, connection);
        await command.ExecuteNonQueryAsync();
    }

    private async Task<string[]> ReadStringsAsync(string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var result = new List<string>();
        while (await reader.ReadAsync()) result.Add(reader.GetString(0));
        return [.. result];
    }

    private async Task ExecuteWithParametersAsync(string sql, params NpgsqlParameter[] parameters)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddRange(parameters);
        await command.ExecuteNonQueryAsync();
    }
}
