using CivicLens.Publication.Contracts;
using Npgsql;

namespace CivicLens.Tests.Host;

public static class PublishedEntryRows
{
    public static async Task<List<PublishedRecordEntry>> ReadAsync(string connectionString)
    {
        await using var connection = new NpgsqlConnection(connectionString);
        await connection.OpenAsync();
        var officials = await ReadChildrenAsync(connection, "published_entry_officials", "official_id || '|' || is_matched");
        var issues = await ReadChildrenAsync(connection, "published_entry_issues", "issue_id");
        var policyAreas = await ReadChildrenAsync(connection, "published_entry_policy_areas", "policy_area_id");
        var entries = new List<PublishedRecordEntry>();
        await using var command = new NpgsqlCommand("""
            SELECT e.kind, e.record_id, e.revision_number, e.headline, p.first_published_at_utc_ticks, e.source_host, e.event_date, e.bill_title,
                e.link_type, e.statement_record_id, e.target_kind, e.target_record_id, e.document_type, e.approved_at_utc_ticks
            FROM published_entries e JOIN published_records p ON p.kind = e.kind AND p.record_id = e.record_id
            ORDER BY e.kind, e.record_id, e.revision_number
            """, connection);
        await using var reader = await command.ExecuteReaderAsync();
        while (await reader.ReadAsync())
        {
            var kind = Enum.Parse<PublishedRecordKind>(reader.GetString(0), ignoreCase: true);
            var key = (reader.GetString(0), reader.GetString(1), reader.GetInt32(2));
            entries.Add(new PublishedRecordEntry
            {
                Kind = kind,
                RecordId = reader.GetString(1),
                RevisionNumber = reader.GetInt32(2),
                Headline = reader.GetString(3),
                FirstPublishedAtUtc = new DateTimeOffset(reader.GetInt64(4), TimeSpan.Zero),
                SourceHost = Text(reader, 5),
                EventDate = reader.IsDBNull(6) ? null : DateOnly.FromDateTime(reader.GetDateTime(6)),
                BillTitle = Text(reader, 7),
                LinkType = Parse<PublishedRelationshipType>(Text(reader, 8)),
                StatementRecordId = Text(reader, 9),
                TargetKind = Parse<PublishedRecordKind>(Text(reader, 10)),
                TargetRecordId = Text(reader, 11),
                MatchedOfficialId = officials[key].Select(value => value.Split('|')).SingleOrDefault(parts => parts[1] == "true")?.ElementAt(0),
                DocumentType = Parse<PublishedExecutiveActionKind>(Text(reader, 12)),
                Officials = [.. officials[key].Select(value => value.Split('|')).Select(parts => new PublishedOfficial { Id = parts[0], Name = string.Empty })],
                ApprovedAtUtc = reader.IsDBNull(13) ? null : new DateTimeOffset(reader.GetInt64(13), TimeSpan.Zero),
                IssueIds = [.. issues[key]],
                PolicyAreaIds = [.. policyAreas[key]]
            });
        }
        return entries;
    }

    private static async Task<ILookup<(string, string, int), string>> ReadChildrenAsync(NpgsqlConnection connection, string table, string value)
    {
        await using var command = new NpgsqlCommand($"SELECT kind, record_id, revision_number, {value} FROM {table} ORDER BY position", connection);
        await using var reader = await command.ExecuteReaderAsync();
        var rows = new List<((string, string, int) Key, string Value)>();
        while (await reader.ReadAsync()) rows.Add(((reader.GetString(0), reader.GetString(1), reader.GetInt32(2)), reader.GetString(3)));
        return rows.ToLookup(row => row.Key, row => row.Value);
    }

    private static string? Text(NpgsqlDataReader reader, int index) => reader.IsDBNull(index) ? null : reader.GetString(index);

    private static T? Parse<T>(string? text) where T : struct, Enum => text is null ? null : Enum.Parse<T>(text, ignoreCase: true);
}
