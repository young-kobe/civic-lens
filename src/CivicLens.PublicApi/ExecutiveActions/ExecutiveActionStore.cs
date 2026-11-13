using System.Text.Json;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Npgsql;
using NpgsqlTypes;

namespace CivicLens.PublicApi.ExecutiveActions;

/// <summary>Reads the views <c>public_api.executive_actions</c>, <c>public_api.executive_action_notices</c>, and <c>public_api.executive_action_targets</c>. One statement reads the record, its notices, and its targets, so all come from one release.</summary>
public sealed class ExecutiveActionStore(NpgsqlDataSource views)
{
    private const int MaximumTargets = 100;

    private const string Sql = """
        SELECT e.release_number, e.record_id, e.revision_number, e.headline, e.kind, e.document_number, e.signed_on, e.published_on,
               e.federal_register_citation, e.document_url, e.official_id, e.president_name, e.disposition_notes,
               (SELECT json_agg(json_build_object('position', n."position", 'documentNumber', n.document_number,
                           'publishedOn', n.published_on, 'url', n.notice_url) ORDER BY n."position")
                  FROM (SELECT n.* FROM public_api.executive_action_notices n
                         WHERE n.record_id = e.record_id AND n.revision_number = e.revision_number
                         ORDER BY n."position" LIMIT @notice_limit) n),
               (SELECT json_agg(json_build_object('position', t."position", 'relation', t.relation, 'kind', t.kind,
                           'number', t.number, 'start', t.reference_start, 'length', t.reference_length,
                           'targetRecordId', t.target_record_id) ORDER BY t."position")
                  FROM (SELECT t.* FROM public_api.executive_action_targets t
                         WHERE t.record_id = e.record_id AND t.revision_number = e.revision_number
                         ORDER BY t."position" LIMIT @target_limit) t)
          FROM public_api.executive_actions e
         WHERE e.record_id = @id
        """;

    /// <summary>Reads one page of the list, newest signature first, and the number of executive actions of the active release in the same query.
    /// Null means that no release is active. The caller compares the release of the page with the release of the cursor.</summary>
    public async Task<ExecutiveActionPage?> ListAsync(ExecutiveActionListRequest request, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(ListSql(request));
        command.Parameters.AddWithValue("take", request.Limit + 1);
        if (request.Kind is { } kind) command.Parameters.AddWithValue("kind", kind.ToString());
        if (request.From is { } from) command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.Date) { Value = from });
        if (request.To is { } to) command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.Date) { Value = to });
        if (request.Cursor is { } cursor)
        {
            command.Parameters.Add(new NpgsqlParameter("cursor_date", NpgsqlDbType.Date) { Value = cursor.SignedOn });
            command.Parameters.AddWithValue("cursor_id", cursor.RecordId);
        }
        var rows = new List<ExecutiveActionRow>();
        int? release = null;
        var total = 0;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                release = reader.GetInt32(0);
                total = ViewColumns.Total(reader);
                if (!reader.IsDBNull(1)) rows.Add(ReadRow(reader));
            }
        if (release is not { } active) return null;
        var page = rows.Take(request.Limit).ToArray();
        return new(active, total, page, rows.Count > request.Limit ? ExecutiveActionCursor.Of(active, page[^1]).Encode() : null);
    }

    private static string ListSql(ExecutiveActionListRequest request) => $"""
        SELECT a.release_number, e.record_id, e.revision_number, e.headline, e.kind, e.document_number, e.signed_on, e.published_on,
               e.federal_register_citation, e.document_url, e.official_id, e.president_name{ViewColumns.TotalColumn("public_api.executive_actions")}
          FROM (SELECT release_number FROM public_api.active_release LIMIT 1) a
          LEFT JOIN LATERAL (
               SELECT e.* FROM public_api.executive_actions e
                WHERE TRUE{Filters(request)}{(request.Cursor is null ? "" : " AND (e.signed_on < @cursor_date OR (e.signed_on = @cursor_date AND e.record_id > @cursor_id))")}
                ORDER BY e.signed_on DESC, e.record_id LIMIT @take) e ON TRUE
         ORDER BY e.signed_on DESC, e.record_id
        """;

    private static string Filters(ExecutiveActionListRequest request) =>
        (request.Kind is null ? "" : " AND e.kind = @kind") +
        (request.From is null ? "" : " AND e.signed_on >= @from") +
        (request.To is null ? "" : " AND e.signed_on <= @to");

    /// <summary>Reads one executive action. Null means that the active release holds no such record.</summary>
    public async Task<ExecutiveActionDetail?> FindAsync(string recordId, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(Sql);
        command.Parameters.AddWithValue("id", recordId);
        command.Parameters.AddWithValue("notice_limit", PublishedExecutiveAction.MaximumNotices + 1);
        command.Parameters.AddWithValue("target_limit", MaximumTargets + 1);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var notes = ViewColumns.Text(reader, 12);
        if (notes?.Length > PublishedDocumentVersion.MaximumTextLength)
            throw new InvalidDataException("Published disposition notes exceed their maximum length.");
        var notices = reader.IsDBNull(13) ? [] : JsonSerializer.Deserialize<ExecutiveActionNoticeRow[]>(reader.GetString(13), PublicationProtocol.JsonOptions)
            ?? throw new InvalidDataException("Published executive notices have no rows.");
        var targets = reader.IsDBNull(14) ? [] : JsonSerializer.Deserialize<TargetSpan[]>(reader.GetString(14), PublicationProtocol.JsonOptions)
            ?? throw new InvalidDataException("Published executive targets have no rows.");
        if (notices.Length > PublishedExecutiveAction.MaximumNotices || targets.Length > MaximumTargets)
            throw new InvalidDataException("Published executive references exceed their maximum count.");
        return new(reader.GetInt32(0), ReadRow(reader), notices,
            [.. targets.Select(target => new ExecutiveActionTargetRow(target.Position, target.Relation, target.Kind, target.Number,
                ReferenceAt(notes, target), target.TargetRecordId))]);
    }

    private static string? ReferenceAt(string? notes, TargetSpan target)
    {
        var reference = notes is null ? null : PublishedDocumentVersion.QuoteAt(notes, target.Start, target.Length);
        if (reference is null) return null;
        var prefix = target.Kind switch
        {
            PublishedExecutiveActionKind.ExecutiveOrder => "EO " + target.Number,
            PublishedExecutiveActionKind.Proclamation => "Proc. " + target.Number,
            _ => null
        };
        return prefix is not null && (reference == prefix || reference.StartsWith(prefix + ", ", StringComparison.Ordinal)) ? reference : null;
    }

    private sealed record TargetSpan(int Position, PublishedExecutiveActionRelation Relation, PublishedExecutiveActionKind Kind, string Number,
        int Start, int Length, string? TargetRecordId);

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
