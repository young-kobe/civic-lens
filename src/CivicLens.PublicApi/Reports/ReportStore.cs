using CivicLens.Publication.Contracts;
using Npgsql;

namespace CivicLens.PublicApi.Reports;

/// <summary>
/// The reader reports in the search database. An insert holds the reports lock until its commit, so the identity sequence commits in order.
/// A pull that reads committed rows therefore never skips a late commit.
/// </summary>
public sealed class ReportStore(NpgsqlDataSource database, PublicApiOptions options)
{
    private const int PurgeAfterDays = 7;

    public async Task<ReportReceipt> StoreAsync(ReportSubmission submission, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ExecuteAsync(connection, transaction, SearchLocks.TakeTransactionLockSql, cancellationToken, ("key", SearchLocks.Reports));

        if (await ScalarAsync(connection, transaction, "SELECT release_number FROM active_release", cancellationToken) is not int release)
            return ReportReceipt.NoRelease;
        if (await ScalarAsync(connection, transaction, "SELECT kind FROM records WHERE record_id = @id AND revision_number = @revision LIMIT 1",
                cancellationToken, ("id", submission.RecordId), ("revision", submission.Revision)) is not string kind)
            return ReportReceipt.UnknownRecord;
        var today = (long)(await ScalarAsync(connection, transaction, "SELECT count(*) FROM reader_reports WHERE received_at > now() - interval '24 hours'",
            cancellationToken))!;
        if (today >= options.ReportsPerDay) return ReportReceipt.Closed;

        await ExecuteAsync(connection, transaction, """
            INSERT INTO reader_reports (record_id, revision_number, kind, release_number, category, body, payload_hash)
            VALUES (@id, @revision, @kind, @release, @category, @body, @hash)
            ON CONFLICT (record_id, revision_number, payload_hash) DO NOTHING
            """,
            cancellationToken, ("id", submission.RecordId), ("revision", submission.Revision), ("kind", kind), ("release", release),
            ("category", submission.Category), ("body", submission.Text), ("hash", submission.PayloadHash));
        await transaction.CommitAsync(cancellationToken);
        return ReportReceipt.Stored;
    }

    /// <summary>
    /// Returns the reports after the cursor and moves the acknowledged sequence forward to it.
    /// A cursor of another epoch, or no cursor, starts from the first report and acknowledges nothing.
    /// </summary>
    public async Task<PulledReportBatch> PullAsync(ReportCursor? after, int limit, CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        var epoch = (Guid)(await ScalarAsync(connection, null, "SELECT epoch FROM report_pull_state", cancellationToken))!;
        var start = after is not null && after.Epoch == epoch ? after.Sequence : 0;
        if (start > 0)
            await ExecuteAsync(connection, null, "UPDATE report_pull_state SET acked_sequence = GREATEST(acked_sequence, @sequence)",
                cancellationToken, ("sequence", start));

        var reports = new List<(long Sequence, PulledReport Report)>();
        await using (var command = Command(connection, null, """
            SELECT sequence, report_id, record_id, revision_number, kind, release_number, category, body, received_at
            FROM reader_reports WHERE sequence > @start ORDER BY sequence LIMIT @limit
            """, ("start", start), ("limit", limit)))
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
                reports.Add((reader.GetInt64(0), new()
                {
                    ReportId = reader.GetGuid(1),
                    RecordId = reader.GetString(2),
                    RevisionNumber = reader.GetInt32(3),
                    Kind = RecordKinds.TryParse(reader.GetString(4), out var kind) ? kind : throw new InvalidDataException("A stored report has an unknown kind."),
                    ReleaseNumber = reader.GetInt32(5),
                    Category = reader.GetString(6),
                    Text = reader.GetString(7),
                    ReceivedAtUtc = reader.GetFieldValue<DateTimeOffset>(8)
                }));
        return new()
        {
            Reports = [.. reports.Select(item => item.Report)],
            NextCursor = new ReportCursor(epoch, reports.Count == 0 ? start : reports[^1].Sequence).Encode()
        };
    }

    /// <summary>Deletes the reports that the puller acknowledged and that are older than the retention time. An unpulled report stays.</summary>
    public async Task<int> PurgeAsync(CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        return await ExecuteAsync(connection, null, $"""
            DELETE FROM reader_reports
            WHERE sequence <= (SELECT acked_sequence FROM report_pull_state) AND received_at < now() - interval '{PurgeAfterDays} days'
            """, cancellationToken);
    }

    private static async Task<object?> ScalarAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql,
        CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        return await command.ExecuteScalarAsync(cancellationToken);
    }

    private static async Task<int> ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql,
        CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var command = Command(connection, transaction, sql, parameters);
        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private static NpgsqlCommand Command(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql, params (string Name, object Value)[] parameters)
    {
        var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        return command;
    }
}
