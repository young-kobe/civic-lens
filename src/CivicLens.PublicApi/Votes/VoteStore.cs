using System.Data.Common;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Npgsql;
using NpgsqlTypes;

namespace CivicLens.PublicApi.Votes;

/// <summary>Reads the views <c>public_api.votes</c> and <c>public_api.vote_issues</c>. Every answer takes its release from the query that reads its rows.</summary>
public sealed class VoteStore(NpgsqlDataSource views)
{
    private const string Columns = """
        v.record_id, v.revision_number, v.chamber, v.congress, v.session_number, v.roll_call_number, v.voted_on, v.kind, v.bill_type, v.bill_number,
        v.headline, v.summary, v.text_origin, v.label, v.question, v.result, v.yea, v.nay, v.present, v.not_voting
        """;
    private const string Order = "v.voted_on DESC, v.chamber, v.session_number DESC, v.roll_call_number DESC, v.record_id";
    // The tuple comparison cannot mix directions, so the chain follows the ORDER BY key by key.
    private const string AfterCursor = """
         AND (v.voted_on < @cursor_date
            OR (v.voted_on = @cursor_date AND v.chamber > @cursor_chamber)
            OR (v.voted_on = @cursor_date AND v.chamber = @cursor_chamber AND v.session_number < @cursor_session)
            OR (v.voted_on = @cursor_date AND v.chamber = @cursor_chamber AND v.session_number = @cursor_session AND v.roll_call_number < @cursor_roll_call)
            OR (v.voted_on = @cursor_date AND v.chamber = @cursor_chamber AND v.session_number = @cursor_session AND v.roll_call_number = @cursor_roll_call
                AND v.record_id > @cursor_id))
        """;

    /// <summary>Reads one page. Null means that no release is active. The caller compares the release of the page with the release of the cursor.</summary>
    public async Task<VotePage?> ListAsync(VoteRequest request, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(ListSql(request));
        AddParameters(command, request);
        var rows = new List<VoteRow>();
        int? release = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                release = reader.GetInt32(0);
                if (!reader.IsDBNull(1)) rows.Add(ReadRow(reader));
            }
        if (release is not { } active) return null;
        var page = rows.Take(request.Limit).ToArray();
        var next = rows.Count > request.Limit ? VoteCursor.Of(active, page[^1]).Encode() : null;
        return new(active, page, next);
    }

    public async Task<VoteDetail?> FindAsync(string recordId, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand($"""
            SELECT v.release_number, {Columns},
                   ARRAY(SELECT i.issue_id FROM public_api.vote_issues i
                          WHERE i.record_id = v.record_id AND i.revision_number = v.revision_number ORDER BY i.issue_id)
              FROM public_api.votes v WHERE v.record_id = @id
            """);
        command.Parameters.AddWithValue("id", recordId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new(reader.GetInt32(0), ReadRow(reader), reader.GetFieldValue<string[]>(21)) : null;
    }

    private static string ListSql(VoteRequest request) => $"""
        SELECT a.release_number, {Columns}
          FROM (SELECT release_number FROM public_api.active_records LIMIT 1) a
          LEFT JOIN LATERAL (
               SELECT v.* FROM public_api.votes v
                WHERE TRUE{Filters(request)}{(request.Cursor is null ? "" : AfterCursor)}
                ORDER BY {Order} LIMIT @take) v
            ON TRUE
         ORDER BY {Order}
        """;

    private static string Filters(VoteRequest request) =>
        (request.Chamber is null ? "" : " AND v.chamber = @chamber") +
        (request.Congress is null ? "" : " AND v.congress = @congress") +
        (request.Session is null ? "" : " AND v.session_number = @session") +
        (request.Kind is null ? "" : " AND v.kind = @kind") +
        (request.BillType is null ? "" : " AND v.bill_type = @bill_type") +
        (request.BillNumber is null ? "" : " AND v.bill_number = @bill_number") +
        (request.Issue is null ? "" : """
             AND EXISTS (SELECT 1 FROM public_api.vote_issues i
                          WHERE i.record_id = v.record_id AND i.revision_number = v.revision_number AND i.issue_id = @issue)
            """) +
        (request.From is null ? "" : " AND v.voted_on >= @from") +
        (request.To is null ? "" : " AND v.voted_on <= @to");

    private static void AddParameters(NpgsqlCommand command, VoteRequest request)
    {
        var parameters = command.Parameters;
        parameters.AddWithValue("take", request.Limit + 1);
        if (request.Chamber is { } chamber) parameters.AddWithValue("chamber", chamber.ToString());
        if (request.Congress is { } congress) parameters.AddWithValue("congress", congress);
        if (request.Session is { } session) parameters.AddWithValue("session", session);
        if (request.Kind is { } kind) parameters.AddWithValue("kind", kind.ToString());
        if (request.BillType is not null) parameters.AddWithValue("bill_type", request.BillType);
        if (request.BillNumber is { } billNumber) parameters.AddWithValue("bill_number", billNumber);
        if (request.Issue is not null) parameters.AddWithValue("issue", request.Issue);
        if (request.From is { } from) parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.Date) { Value = from });
        if (request.To is { } to) parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.Date) { Value = to });
        if (request.Cursor is not { } cursor) return;
        parameters.Add(new NpgsqlParameter("cursor_date", NpgsqlDbType.Date) { Value = cursor.VotedOn });
        parameters.AddWithValue("cursor_chamber", cursor.Chamber.ToString());
        parameters.AddWithValue("cursor_session", cursor.Session);
        parameters.AddWithValue("cursor_roll_call", cursor.RollCall);
        parameters.AddWithValue("cursor_id", cursor.RecordId);
    }

    // Column 0 holds the release. The row starts at column 1.
    private static VoteRow ReadRow(DbDataReader reader) => new()
    {
        RecordId = reader.GetString(1),
        RevisionNumber = reader.GetInt32(2),
        Chamber = ViewColumns.Enum<PublishedChamber>(reader, 3),
        Congress = reader.GetInt32(4),
        Session = reader.GetInt32(5),
        RollCall = reader.GetInt32(6),
        VotedOn = reader.GetFieldValue<DateOnly>(7),
        Kind = ViewColumns.Enum<PublishedVoteKind>(reader, 8),
        BillType = ViewColumns.Text(reader, 9),
        BillNumber = ViewColumns.Number(reader, 10),
        Headline = reader.GetString(11),
        Summary = reader.GetString(12),
        TextOrigin = ViewColumns.Enum<PublishedTextOrigin>(reader, 13),
        Label = ViewColumns.Text(reader, 14),
        Question = ViewColumns.Text(reader, 15),
        Result = reader.GetString(16),
        Yea = ViewColumns.Number(reader, 17),
        Nay = ViewColumns.Number(reader, 18),
        Present = ViewColumns.Number(reader, 19),
        NotVoting = ViewColumns.Number(reader, 20)
    };
}
