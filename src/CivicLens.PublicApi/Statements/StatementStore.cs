using System.Data.Common;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Npgsql;
using NpgsqlTypes;

namespace CivicLens.PublicApi.Statements;

public sealed class StatementStore(NpgsqlDataSource views)
{
    public async Task<StatementPage?> ListAsync(StatementRequest request, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand($"""
            SELECT a.release_number, s.record_id, s.revision_number, s.statement_date, s.headline, s.summary, s.text_origin, s.label,
                   ARRAY(SELECT p.official_id FROM public_api.statement_speakers p
                          WHERE p.record_id = s.record_id AND p.revision_number = s.revision_number ORDER BY p."position"),
                   s.issue_ids
              FROM (SELECT release_number FROM public_api.active_records LIMIT 1) a
              LEFT JOIN LATERAL (
                   SELECT s.* FROM public_api.statements s
                    WHERE TRUE{Filters(request)}{Cursor(request)}
                    ORDER BY s.statement_date DESC NULLS LAST, s.record_id LIMIT @take) s ON TRUE
             ORDER BY s.statement_date DESC NULLS LAST, s.record_id
            """);
        command.Parameters.AddWithValue("take", request.Limit + 1);
        if (request.Official is not null) command.Parameters.AddWithValue("official", request.Official);
        if (request.Issue is not null) command.Parameters.AddWithValue("issue", request.Issue);
        if (request.From is { } from) command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.Date) { Value = from });
        if (request.To is { } to) command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.Date) { Value = to });
        if (request.Cursor is { } cursor)
        {
            command.Parameters.AddWithValue("cursor_id", cursor.RecordId);
            if (cursor.StatementDate is { } date) command.Parameters.Add(new NpgsqlParameter("cursor_date", NpgsqlDbType.Date) { Value = date });
        }
        var rows = new List<StatementRow>();
        int? release = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                release = reader.GetInt32(0);
                if (!reader.IsDBNull(1)) rows.Add(ReadRow(reader));
            }
        if (release is not { } active) return null;
        var page = rows.Take(request.Limit).ToArray();
        var next = rows.Count > request.Limit ? StatementCursor.Of(active, page[^1]).Encode() : null;
        return new(active, page, next);
    }

    private const string AfterDated = " AND (s.statement_date < @cursor_date OR s.statement_date IS NULL OR (s.statement_date = @cursor_date AND s.record_id > @cursor_id))";
    private const string AfterUndated = " AND s.statement_date IS NULL AND s.record_id > @cursor_id";

    private static string Cursor(StatementRequest request) => request.Cursor switch
    {
        null => "",
        { StatementDate: null } => AfterUndated,
        _ => AfterDated
    };
    // OFFSET 0 keeps the speaker lookup correlated through the security barrier view.
    private static string Filters(StatementRequest request) =>
        (request.Official is null ? "" : " AND EXISTS (SELECT 1 FROM public_api.statement_speakers p WHERE p.record_id = s.record_id AND p.revision_number = s.revision_number AND p.official_id = @official OFFSET 0)") +
        (request.Issue is null ? "" : " AND @issue = ANY(s.issue_ids)") +
        (request.From is null ? "" : " AND s.statement_date >= @from") +
        (request.To is null ? "" : " AND s.statement_date <= @to");

    private static StatementRow ReadRow(DbDataReader reader) => new()
    {
        RecordId = reader.GetString(1),
        RevisionNumber = reader.GetInt32(2),
        StatementDate = ViewColumns.Date(reader, 3),
        Headline = reader.GetString(4),
        Summary = reader.GetString(5),
        TextOrigin = ViewColumns.Enum<PublishedTextOrigin>(reader, 6),
        Label = ViewColumns.Text(reader, 7),
        SpeakerIds = reader.GetFieldValue<string[]>(8),
        IssueIds = reader.GetFieldValue<string[]>(9)
    };
}
