using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Npgsql;
using NpgsqlTypes;

namespace CivicLens.PublicApi.Records;

/// <summary>Reads the view <c>public_api.record_links</c> and <c>public_api.active_records</c>, with identity from <c>public_api.active_release</c>. Every answer takes its release from the query that reads its rows.</summary>
public sealed class RelatedStore(NpgsqlDataSource views)
{
    private const string Order = "l.statement_date NULLS LAST, l.link_record_id";
    private const string AfterDated = " AND (l.statement_date > @cursor_date OR l.statement_date IS NULL OR (l.statement_date = @cursor_date AND l.link_record_id > @cursor_id))";
    private const string AfterUndated = " AND l.statement_date IS NULL AND l.link_record_id > @cursor_id";

    /// <summary>Reads one page. Null means that no release is active or that the active release holds no such record. The caller compares the release of the page with the release of the cursor.</summary>
    public async Task<RelatedPage?> ListAsync(RelatedRequest request, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(Sql(request));
        AddParameters(command, request);
        var rows = new List<RelatedRecord>();
        int? release = null;
        var recordExists = false;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                release = reader.GetInt32(0);
                recordExists = reader.GetBoolean(1);
                if (!reader.IsDBNull(2)) rows.Add(ReadRow(reader));
            }
        if (release is not { } active || !recordExists) return null;
        var page = rows.Take(request.Limit).ToArray();
        var next = rows.Count > request.Limit ? RelatedCursor.Of(active, page[^1]).Encode() : null;
        return new(active, page, next);
    }

    private static string Sql(RelatedRequest request) => $"""
        SELECT a.release_number,
               EXISTS (SELECT 1 FROM public_api.active_records x WHERE x.kind = @kind AND x.record_id = @id),
               l.related_kind, l.related_record_id, l.related_headline, l.related_text_origin, l.related_label, l.link_type, l.statement_date, l.link_record_id, l.reason
          FROM (SELECT release_number FROM public_api.active_release LIMIT 1) a
          LEFT JOIN LATERAL (
               SELECT l.* FROM public_api.record_links l
                WHERE l.kind = @kind AND l.record_id = @id{Cursor(request)}
                ORDER BY {Order} LIMIT @take) l
            ON TRUE
         ORDER BY {Order}
        """;

    private static string Cursor(RelatedRequest request) => request.Cursor switch
    {
        null => "",
        { StatementDate: null } => AfterUndated,
        _ => AfterDated
    };

    private static void AddParameters(NpgsqlCommand command, RelatedRequest request)
    {
        var parameters = command.Parameters;
        parameters.AddWithValue("kind", PublicationProtocol.KindText(request.Kind));
        parameters.AddWithValue("id", request.RecordId);
        parameters.AddWithValue("take", request.Limit + 1);
        if (request.Cursor is not { } cursor) return;
        parameters.AddWithValue("cursor_id", cursor.LinkRecordId);
        if (cursor.StatementDate is { } date) parameters.Add(new NpgsqlParameter("cursor_date", NpgsqlDbType.Date) { Value = date });
    }

    // Column 0 holds the release and column 1 holds whether the record exists. The row starts at column 2.
    private static RelatedRecord ReadRow(NpgsqlDataReader reader) => new()
    {
        Kind = RecordKinds.TryParse(reader.GetString(2), out var kind) ? kind : throw new InvalidDataException("The view holds an unknown record kind."),
        RecordId = reader.GetString(3),
        Headline = reader.GetString(4),
        TextOrigin = ViewColumns.Enum<PublishedTextOrigin>(reader, 5),
        Label = ViewColumns.Text(reader, 6),
        LinkType = ViewColumns.Enum<PublishedRelationshipType>(reader, 7),
        StatementDate = ViewColumns.Date(reader, 8),
        LinkRecordId = reader.GetString(9),
        Reason = ViewColumns.Text(reader, 10)
    };
}
