using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Npgsql;

namespace CivicLens.PublicApi.Ingest;

/// <summary>
/// Rebuilds the manifest entries of record revisions from the entry views. One query reads each view for the whole batch.
/// The names of officials come from <c>release_officials</c> of the active release.
/// </summary>
public sealed class TypedEntryReader(NpgsqlDataSource views)
{
    public async Task<Dictionary<(string RecordId, int Revision), PublishedRecordEntry>> ReadAsync(
        RevisionKeys keys, int releaseNumber, CancellationToken cancellationToken)
    {
        var officials = await ReadOfficialsAsync(keys, releaseNumber, cancellationToken);
        var issues = await ReadValuesAsync(keys, IssuesSql, cancellationToken);
        var areas = await ReadValuesAsync(keys, PolicyAreasSql, cancellationToken);
        await using var command = keys.Command(views, $"""
            SELECT x.kind, x.record_id, x.revision_number, x.headline, x.source_host, x.event_date, x.bill_line, x.bill_title, x.document_type,
                   x.link_type, x.statement_record_id, x.target_kind, x.target_record_id, f.first_published_at_utc_ticks
              FROM public_api.record_entries x
              JOIN public_api.record_first_publications f ON f.kind = x.kind AND f.record_id = x.record_id
             WHERE {RevisionKeys.Filter}
            """);
        var entries = new Dictionary<(string, int), PublishedRecordEntry>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var key = (reader.GetString(1), reader.GetInt32(2));
            var people = officials.GetValueOrDefault(key) ?? [];
            entries[key] = new()
            {
                Kind = ParseKind(reader.GetString(0)),
                RecordId = key.Item1,
                RevisionNumber = key.Item2,
                Headline = reader.GetString(3),
                FirstPublishedAtUtc = new DateTimeOffset(reader.GetInt64(13), TimeSpan.Zero),
                Officials = [.. people.Select(person => person.Official)],
                IssueIds = issues.GetValueOrDefault(key) ?? [],
                PolicyAreaIds = areas.GetValueOrDefault(key) ?? [],
                SourceHost = ViewColumns.Text(reader, 4),
                EventDate = ViewColumns.Date(reader, 5),
                BillLine = ViewColumns.Text(reader, 6),
                BillTitle = ViewColumns.Text(reader, 7),
                DocumentType = reader.IsDBNull(8) ? null : ViewColumns.Enum<PublishedExecutiveActionKind>(reader, 8),
                LinkType = reader.IsDBNull(9) ? null : ViewColumns.Enum<PublishedRelationshipType>(reader, 9),
                StatementRecordId = ViewColumns.Text(reader, 10),
                TargetKind = reader.IsDBNull(11) ? null : ParseKind(reader.GetString(11)),
                TargetRecordId = ViewColumns.Text(reader, 12),
                MatchedOfficialId = people.FirstOrDefault(person => person.Matched).Official?.Id
            };
        }
        return entries;
    }

    private static PublishedRecordKind ParseKind(string text) =>
        RecordKinds.TryParse(text, out var kind) ? kind : throw new InvalidDataException("The view holds an unknown record kind.");

    private async Task<Dictionary<(string, int), (PublishedOfficial Official, bool Matched)[]>> ReadOfficialsAsync(
        RevisionKeys keys, int releaseNumber, CancellationToken cancellationToken)
    {
        await using var command = keys.Command(views, $"""
            SELECT x.record_id, x.revision_number, x.official_id, n.name, x.is_matched
              FROM public_api.record_officials x
              LEFT JOIN public_api.release_officials n ON n.release_number = @release AND n.official_id = x.official_id
             WHERE {RevisionKeys.Filter}
             ORDER BY x.record_id, x.revision_number, x."position"
            """);
        command.Parameters.AddWithValue("release", releaseNumber);
        var rows = new List<(string Id, int Revision, PublishedOfficial Official, bool Matched)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            rows.Add((reader.GetString(0), reader.GetInt32(1),
                new PublishedOfficial { Id = reader.GetString(2), Name = ViewColumns.Text(reader, 3) ?? throw new InvalidDataException("An official of the record has no name in the release.") },
                reader.GetBoolean(4)));
        return rows.GroupBy(row => (row.Id, row.Revision)).ToDictionary(group => group.Key, group => group.Select(row => (row.Official, row.Matched)).ToArray());
    }

    private static readonly string IssuesSql = $"""
        SELECT x.record_id, x.revision_number, x.issue_id FROM public_api.record_issues x WHERE {RevisionKeys.Filter}
         ORDER BY x.record_id, x.revision_number, x."position"
        """;

    private static readonly string PolicyAreasSql = $"""
        SELECT x.record_id, x.revision_number, x.policy_area_id FROM public_api.record_policy_areas x WHERE {RevisionKeys.Filter}
         ORDER BY x.record_id, x.revision_number, x."position"
        """;

    private async Task<Dictionary<(string, int), string[]>> ReadValuesAsync(RevisionKeys keys, string sql, CancellationToken cancellationToken)
    {
        await using var command = keys.Command(views, sql);
        var rows = new List<(string Id, int Revision, string Value)>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken)) rows.Add((reader.GetString(0), reader.GetInt32(1), reader.GetString(2)));
        return rows.GroupBy(row => (row.Id, row.Revision)).ToDictionary(group => group.Key, group => group.Select(row => row.Value).ToArray());
    }
}
