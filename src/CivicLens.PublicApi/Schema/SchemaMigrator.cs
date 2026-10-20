using System.Globalization;
using System.Reflection;
using Npgsql;

namespace CivicLens.PublicApi.Schema;

/// <summary>Applies the numbered SQL files in this folder in order, once each, under one advisory lock.</summary>
public sealed class SchemaMigrator(NpgsqlDataSource database)
{
    private const string ResourcePrefix = "schema/";

    public async Task ApplyAsync(CancellationToken cancellationToken)
    {
        await using var connection = await database.OpenConnectionAsync(cancellationToken);
        await ExecuteAsync(connection, null, "SELECT pg_advisory_lock(hashtextextended(@key, 0))", cancellationToken, ("key", SearchLocks.Schema));
        try
        {
            await ExecuteAsync(connection, null,
                "CREATE TABLE IF NOT EXISTS schema_migrations (version integer PRIMARY KEY, name text NOT NULL, applied_at_utc timestamptz NOT NULL DEFAULT now())",
                cancellationToken);
            var applied = await AppliedVersionsAsync(connection, cancellationToken);
            foreach (var migration in Migrations().Where(migration => !applied.Contains(migration.Version)))
                await ApplyAsync(connection, migration, cancellationToken);
        }
        finally
        {
            await ExecuteAsync(connection, null, "SELECT pg_advisory_unlock(hashtextextended(@key, 0))", CancellationToken.None, ("key", SearchLocks.Schema));
        }
    }

    private static async Task ApplyAsync(NpgsqlConnection connection, Migration migration, CancellationToken cancellationToken)
    {
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
        await ExecuteAsync(connection, transaction, migration.Sql, cancellationToken);
        await ExecuteAsync(connection, transaction, "INSERT INTO schema_migrations (version, name) VALUES (@version, @name)",
            cancellationToken, ("version", migration.Version), ("name", migration.Name));
        await transaction.CommitAsync(cancellationToken);
    }

    private static async Task<HashSet<int>> AppliedVersionsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT version FROM schema_migrations", connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var versions = new HashSet<int>();
        while (await reader.ReadAsync(cancellationToken)) versions.Add(reader.GetInt32(0));
        return versions;
    }

    private static IEnumerable<Migration> Migrations()
    {
        var assembly = Assembly.GetExecutingAssembly();
        return assembly.GetManifestResourceNames().Where(name => name.StartsWith(ResourcePrefix, StringComparison.Ordinal))
            .Select(name => Read(assembly, name)).OrderBy(migration => migration.Version);
    }

    private static Migration Read(Assembly assembly, string resource)
    {
        var name = Path.GetFileNameWithoutExtension(resource[ResourcePrefix.Length..]);
        var version = int.Parse(name[..name.IndexOf('_', StringComparison.Ordinal)], CultureInfo.InvariantCulture);
        using var reader = new StreamReader(assembly.GetManifestResourceStream(resource)!);
        return new(version, name, reader.ReadToEnd());
    }

    private static async Task ExecuteAsync(NpgsqlConnection connection, NpgsqlTransaction? transaction, string sql,
        CancellationToken cancellationToken, params (string Name, object Value)[] parameters)
    {
        await using var command = new NpgsqlCommand(sql, connection, transaction);
        foreach (var (name, value) in parameters) command.Parameters.AddWithValue(name, value);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    private sealed record Migration(int Version, string Name, string Sql);
}
