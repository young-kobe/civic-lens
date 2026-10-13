using Npgsql;

namespace CivicLens.Tests.Fixtures;

internal sealed class HeldAdvisoryLock : IAsyncDisposable
{
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
        await using var command = new NpgsqlCommand($"SELECT pg_advisory_xact_lock(hashtextextended('{key}', 0))", connection, transaction);
        await command.ExecuteNonQueryAsync();
        return new(connection, transaction);
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
