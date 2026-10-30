using System.Text.Json;
using Npgsql;

namespace CivicLens.Tests.Fixtures;

public static class PublishedRecordStateReader
{
    public static async Task<SortedSet<string>> ReadReleaseNumbersAsync(string connectionString) =>
        [.. (await ReadAsync(connectionString, "SELECT release_number::text FROM publication_releases")).Select(row => row[0])];

    public static async Task<SortedSet<string>> ReadFromRecordsJsonAsync(string connectionString, int release)
    {
        var json = await ReadAsync(connectionString, $"SELECT records_json FROM publication_releases WHERE release_number = {release}");
        using var document = JsonDocument.Parse(json.Single()[0]);
        return [.. document.RootElement.EnumerateArray().Select(entry => string.Join('|',
            entry.TryGetProperty("kind", out var kind) ? kind.GetString() : "documentChange",
            entry.TryGetProperty("recordId", out var recordId) ? recordId.GetString() : entry.GetProperty("draftId").GetString(),
            entry.GetProperty("revisionNumber").GetInt32()))];
    }

    public static async Task<SortedSet<string>> ReadFromStateTableAsync(string connectionString, int release) =>
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

    public static async Task AssertEveryReleaseMatchesAsync(string connectionString)
    {
        var releases = await ReadReleaseNumbersAsync(connectionString);
        Assert.NotEmpty(releases);
        foreach (var release in releases.Select(int.Parse))
            Assert.Equal(await ReadFromRecordsJsonAsync(connectionString, release), await ReadFromStateTableAsync(connectionString, release));
    }
}
