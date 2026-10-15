using Npgsql;

namespace CivicLens.Tests.Fixtures;

internal sealed class HeldAdvisoryLock : IAsyncDisposable
{
    private static readonly TimeSpan WaiterTimeout = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan PollInterval = TimeSpan.FromMilliseconds(10);

    private readonly NpgsqlConnection connection;
    private readonly NpgsqlTransaction transaction;
    private bool released;

    private HeldAdvisoryLock(NpgsqlConnection connection, NpgsqlTransaction transaction)
    {
        this.connection = connection;
        this.transaction = transaction;
    }

    public static async Task<HeldAdvisoryLock> AcquireAsync(string connectionString, string key)
    {
        var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var transaction = await connection.BeginTransactionAsync();
        await using var command = new NpgsqlCommand("SELECT pg_advisory_xact_lock(hashtextextended(@key, 0))", connection, transaction);
        command.Parameters.AddWithValue("key", key);
        await command.ExecuteNonQueryAsync();
        return new(connection, transaction);
    }

    public static async Task WaitForWaiterAsync(string connectionString, string key)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var deadline = DateTime.UtcNow + WaiterTimeout;
        while (!await AnyBackendWaitsAsync(connection, key))
        {
            if (DateTime.UtcNow > deadline) throw new TimeoutException($"No backend waited on advisory lock '{key}' within {WaiterTimeout}.");
            await Task.Delay(PollInterval);
        }
    }

    private static async Task<bool> AnyBackendWaitsAsync(NpgsqlConnection connection, string key)
    {
        await using var command = new NpgsqlCommand("""
            SELECT EXISTS (SELECT 1 FROM pg_locks
                WHERE locktype = 'advisory' AND objsubid = 1 AND NOT granted
                  AND database = (SELECT oid FROM pg_database WHERE datname = current_database())
                  AND ((classid::bigint << 32) | objid::bigint) = hashtextextended(@key, 0))
            """, connection);
        command.Parameters.AddWithValue("key", key);
        return (bool)(await command.ExecuteScalarAsync())!;
    }

    public async ValueTask DisposeAsync()
    {
        if (released) return;
        released = true;
        await transaction.CommitAsync();
        await transaction.DisposeAsync();
        await connection.DisposeAsync();
    }
}
