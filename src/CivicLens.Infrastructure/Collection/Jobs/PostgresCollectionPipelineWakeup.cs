using System.Data;
using CivicLens.Application.Collection.Jobs;
using Npgsql;

namespace CivicLens.Infrastructure.Collection.Jobs;

/// <summary>Dedicated PostgreSQL LISTEN connection with durable deadline wakeups.</summary>
public sealed class PostgresCollectionPipelineWakeup(string connectionString) : ICollectionPipelineWakeup
{
    private const string Channel = "civic_lens_work";
    private NpgsqlConnection? connection;
    private bool hasNotification;
    private long? notifiedDeadline;
    private long databaseTicksWatermark;

    public static PostgresCollectionPipelineWakeup FromConnectionString(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        return new(connectionString);
    }

    public async Task ConnectAsync(CancellationToken cancellationToken)
    {
        await DisposeConnectionAsync();
        var opened = new NpgsqlConnection(connectionString);
        try
        {
            await opened.OpenAsync(cancellationToken);
            opened.Notification += OnNotification;
            await using var command = new NpgsqlCommand($"LISTEN {Channel}", opened);
            await command.ExecuteNonQueryAsync(cancellationToken);
            var watermark = await ReadDatabaseTicksAsync(opened, cancellationToken);
            connection = opened;
            hasNotification = false;
            notifiedDeadline = null;
            databaseTicksWatermark = watermark;
        }
        catch
        {
            await opened.DisposeAsync();
            throw;
        }
    }

    public async Task WaitAsync(CancellationToken cancellationToken)
    {
        var current = connection ?? throw new InvalidOperationException("Pipeline wakeup is not connected.");
        while (true)
        {
            var deadline = await ReadNextDeadlineAsync(current, databaseTicksWatermark, cancellationToken);
            databaseTicksWatermark = deadline.DatabaseTicks;
            var ticksUntilDue = deadline.TicksUntilDue;
            if (notifiedDeadline is { } notified)
            {
                ticksUntilDue = Math.Min(ticksUntilDue ?? long.MaxValue, notified - deadline.DatabaseTicks);
            }
            if (hasNotification || ticksUntilDue is <= 0)
            {
                hasNotification = false;
                notifiedDeadline = null;
                return;
            }
            // Npgsql treats zero milliseconds as infinite. Round future deadlines up to at least one.
            var timeout = ticksUntilDue is null ? Timeout.Infinite :
                (int)Math.Clamp(Math.Ceiling(ticksUntilDue.Value / (double)TimeSpan.TicksPerMillisecond), 1, int.MaxValue);
            if (!await current.WaitAsync(timeout, cancellationToken))
            {
                notifiedDeadline = null;
                return;
            }
            // A release notification can describe a future barrier. Consume it without draining
            // blocked work; any immediate work notification still wakes the pipeline here.
        }
    }

    public async ValueTask DisposeAsync() => await DisposeConnectionAsync();

    private void OnNotification(object sender, NpgsqlNotificationEventArgs eventArgs)
    {
        if (long.TryParse(eventArgs.Payload, System.Globalization.NumberStyles.None,
                System.Globalization.CultureInfo.InvariantCulture, out var ticks) && ticks > 0 && ticks <= DateTime.MaxValue.Ticks)
            notifiedDeadline = Math.Min(notifiedDeadline ?? long.MaxValue, ticks);
        else
            hasNotification = true;
    }

    private static async Task<(long DatabaseTicks, long? TicksUntilDue)> ReadNextDeadlineAsync(
        NpgsqlConnection connection, long watermark, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("""
            WITH database_now AS (SELECT (EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968000000000)::bigint AS ticks),
            deadlines AS (
                SELECT retry_at AS ticks FROM collection_jobs
                 WHERE state = 'WaitingToRetry' AND retry_at IS NOT NULL
                UNION ALL
                SELECT lease_expires_at FROM collection_jobs
                 WHERE lease_expires_at IS NOT NULL
                UNION ALL
                SELECT retry_at FROM evidence_processing
                 WHERE status = 'RetryWaiting' AND retry_at IS NOT NULL
                UNION ALL
                SELECT lease_expires_at FROM evidence_processing
                 WHERE lease_expires_at IS NOT NULL
                UNION ALL
                SELECT expires_at FROM collection_collector_slot
                 WHERE expires_at IS NOT NULL
                UNION ALL
                SELECT not_before FROM collection_job_origins
            )
            SELECT database_now.ticks, min(deadlines.ticks) - database_now.ticks
              FROM database_now LEFT JOIN deadlines ON deadlines.ticks > @watermark
             GROUP BY database_now.ticks
            """, connection);
        command.Parameters.AddWithValue("watermark", watermark);
        await using var reader = await command.ExecuteReaderAsync(CommandBehavior.SingleRow, cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) throw new InvalidOperationException("Postgres did not return its clock.");
        return (reader.GetInt64(0), reader.IsDBNull(1) ? null : reader.GetInt64(1));
    }

    private static async Task<long> ReadDatabaseTicksAsync(NpgsqlConnection connection,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(
            "SELECT (EXTRACT(EPOCH FROM clock_timestamp())::numeric * 10000000 + 621355968000000000)::bigint", connection);
        return Convert.ToInt64(await command.ExecuteScalarAsync(cancellationToken),
            System.Globalization.CultureInfo.InvariantCulture);
    }

    private async Task DisposeConnectionAsync()
    {
        if (connection is null) return;
        connection.Notification -= OnNotification;
        await connection.DisposeAsync();
        connection = null;
    }
}
