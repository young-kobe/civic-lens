using Npgsql;

namespace CivicLens.Tests.Infrastructure.Publication;

public static class PublishedRecordStateReader
{
    public static async Task<SortedSet<string>> StateAtAsync(string connectionString, int release) =>
        [.. (await ReadAsync(connectionString, $"""
            SELECT kind || '|' || record_id || '|' || revision_number FROM publication_record_states
            WHERE first_release <= {release} AND (last_release IS NULL OR last_release >= {release})
            """)).Select(row => row[0])];

    public static async Task<List<string[]>> ReadAsync(string connectionString, string sql)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<string[]>();
        while (await reader.ReadAsync())
            rows.Add([.. Enumerable.Range(0, reader.FieldCount).Select(index => reader.GetValue(index).ToString() ?? "NULL")]);
        return rows;
    }

    public static async Task<List<string>> RangesAsync(string connectionString, string recordId) =>
        [.. (await ReadAsync(connectionString, $"""
            SELECT first_release || '|' || COALESCE(last_release::text, 'NULL') FROM publication_record_states
            WHERE record_id = '{recordId}' ORDER BY first_release
            """)).Select(row => row[0])];
}
