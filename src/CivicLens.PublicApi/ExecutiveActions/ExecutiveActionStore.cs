using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Npgsql;

namespace CivicLens.PublicApi.ExecutiveActions;

/// <summary>Reads the views <c>public_api.executive_actions</c> and <c>public_api.executive_action_notices</c>. One statement reads the record and its notices, so both come from one release.</summary>
public sealed class ExecutiveActionStore(NpgsqlDataSource views)
{
    private const string Sql = """
        SELECT e.release_number, e.record_id, e.revision_number, e.headline, e.kind, e.document_number, e.signed_on, e.published_on,
               e.federal_register_citation, e.document_url, e.official_id, e.president_name,
               n."position", n.document_number, n.published_on, n.notice_url
          FROM public_api.executive_actions e
          LEFT JOIN public_api.executive_action_notices n ON n.record_id = e.record_id AND n.revision_number = e.revision_number
         WHERE e.record_id = @id
         ORDER BY n."position"
        """;

    /// <summary>Reads one executive action. Null means that the active release holds no such record.</summary>
    public async Task<ExecutiveActionDetail?> FindAsync(string recordId, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(Sql);
        command.Parameters.AddWithValue("id", recordId);
        ExecutiveActionRow? row = null;
        var release = 0;
        var notices = new List<ExecutiveActionNoticeRow>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            release = reader.GetInt32(0);
            row ??= ReadRow(reader);
            if (!reader.IsDBNull(12)) notices.Add(new(reader.GetInt32(12), reader.GetString(13), reader.GetFieldValue<DateOnly>(14), reader.GetString(15)));
        }
        return row is null ? null : new(release, row, [.. notices]);
    }

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
