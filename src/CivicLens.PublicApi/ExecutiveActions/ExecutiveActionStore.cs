using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Npgsql;

namespace CivicLens.PublicApi.ExecutiveActions;

/// <summary>Reads the views <c>public_api.executive_actions</c>, <c>public_api.executive_action_notices</c>, and <c>public_api.executive_action_targets</c>. One statement reads the record, its notices, and its targets, so all come from one release.</summary>
public sealed class ExecutiveActionStore(NpgsqlDataSource views)
{
    private const string Sql = """
        SELECT e.release_number, e.record_id, e.revision_number, e.headline, e.kind, e.document_number, e.signed_on, e.published_on,
               e.federal_register_citation, e.document_url, e.official_id, e.president_name,
               n."position", n.document_number, n.published_on, n.notice_url,
               t."position", t.relation, t.kind, t.number, t.reference, t.target_record_id
          FROM public_api.executive_actions e
          LEFT JOIN public_api.executive_action_notices n ON n.record_id = e.record_id AND n.revision_number = e.revision_number
          LEFT JOIN public_api.executive_action_targets t ON t.record_id = e.record_id AND t.revision_number = e.revision_number
         WHERE e.record_id = @id
         ORDER BY n."position", t."position"
        """;

    /// <summary>Reads one executive action. Null means that the active release holds no such record.</summary>
    public async Task<ExecutiveActionDetail?> FindAsync(string recordId, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(Sql);
        command.Parameters.AddWithValue("id", recordId);
        ExecutiveActionRow? row = null;
        var release = 0;
        var notices = new SortedDictionary<int, ExecutiveActionNoticeRow>();
        var targets = new SortedDictionary<int, ExecutiveActionTargetRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            release = reader.GetInt32(0);
            row ??= ReadRow(reader);
            // The two joins repeat each notice for each target, so a position keeps one row of each.
            if (!reader.IsDBNull(12)) notices.TryAdd(reader.GetInt32(12), new(reader.GetInt32(12), reader.GetString(13), reader.GetFieldValue<DateOnly>(14), reader.GetString(15)));
            if (!reader.IsDBNull(16)) targets.TryAdd(reader.GetInt32(16), ReadTarget(reader));
        }
        return row is null ? null : new(release, row, [.. notices.Values], [.. targets.Values]);
    }

    private static ExecutiveActionTargetRow ReadTarget(NpgsqlDataReader reader) => new(reader.GetInt32(16),
        ViewColumns.Enum<PublishedExecutiveActionRelation>(reader, 17), ViewColumns.Enum<PublishedExecutiveActionKind>(reader, 18),
        reader.GetString(19), reader.IsDBNull(20) ? null : reader.GetString(20), reader.IsDBNull(21) ? null : reader.GetString(21));

    // Column 0 holds the release. The row starts at column 1.
    private static ExecutiveActionRow ReadRow(NpgsqlDataReader reader) => new()
    {
        RecordId = reader.GetString(1),
        RevisionNumber = reader.GetInt32(2),
        Headline = reader.GetString(3),
        Kind = ViewColumns.Enum<PublishedExecutiveActionKind>(reader, 4),
        DocumentNumber = reader.GetString(5),
        SignedOn = reader.GetFieldValue<DateOnly>(6),
        PublishedOn = reader.GetFieldValue<DateOnly>(7),
        FederalRegisterCitation = reader.GetString(8),
        DocumentUrl = reader.GetString(9),
        OfficialId = reader.GetString(10),
        PresidentName = reader.GetString(11)
    };
}
