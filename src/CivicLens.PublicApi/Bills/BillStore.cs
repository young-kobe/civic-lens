using System.Data.Common;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Npgsql;
using NpgsqlTypes;

namespace CivicLens.PublicApi.Bills;

/// <summary>Reads the views <c>public_api.bills</c>, <c>public_api.bill_sponsorships</c>, and <c>public_api.bill_issues</c>. Every answer takes its release from the query that reads its rows.</summary>
public sealed class BillStore(NpgsqlDataSource views)
{
    private const string Columns = """
        b.record_id, b.revision_number, b.congress, b.bill_type, b.bill_number, b.bill_title, b.headline, b.summary, b.text_origin, b.label,
        b.policy_area, b.policy_area_id, b.event_date, b.sponsor_count, b.cosponsor_count
        """;
    private const string Order = "b.event_date DESC, b.record_id";
    private const string AfterCursor = " AND (b.event_date < @cursor_date OR (b.event_date = @cursor_date AND b.record_id > @cursor_id))";

    /// <summary>Reads one page. Null means that no release is active. The caller compares the release of the page with the release of the cursor.</summary>
    public async Task<BillPage?> ListAsync(BillRequest request, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(ListSql(request));
        AddParameters(command, request);
        var rows = new List<BillRow>();
        int? release = null;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                release = reader.GetInt32(0);
                if (!reader.IsDBNull(1)) rows.Add(ReadRow(reader));
            }
        if (release is not { } active) return null;
        var page = rows.Take(request.Limit).ToArray();
        var next = rows.Count > request.Limit ? BillCursor.Of(active, page[^1]).Encode() : null;
        return new(active, page, next);
    }

    public async Task<BillDetail?> FindAsync(string recordId, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand($"""
            SELECT b.release_number, {Columns},
                   ARRAY(SELECT i.issue_id FROM public_api.bill_issues i
                          WHERE i.record_id = b.record_id AND i.revision_number = b.revision_number ORDER BY i."position")
              FROM public_api.bills b WHERE b.record_id = @id
            """);
        command.Parameters.AddWithValue("id", recordId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) ? new(reader.GetInt32(0), ReadRow(reader), reader.GetFieldValue<string[]>(16)) : null;
    }

    /// <summary>Reads one page of the sponsorships of a bill. Null means that no release is active or that the active release holds no such bill.</summary>
    public async Task<SponsorshipPage?> ListSponsorshipsAsync(SponsorshipRequest request, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(SponsorshipSql(request));
        command.Parameters.AddWithValue("id", request.RecordId);
        command.Parameters.AddWithValue("take", request.Limit + 1);
        if (request.Role is { } role) command.Parameters.AddWithValue("role", role.ToString());
        if (request.Cursor is { } cursor) command.Parameters.AddWithValue("cursor_position", cursor.Position);
        var rows = new List<SponsorshipRow>();
        int? release = null;
        var billExists = false;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                release = reader.GetInt32(0);
                billExists = reader.GetBoolean(1);
                if (!reader.IsDBNull(2)) rows.Add(ReadSponsorship(reader));
            }
        if (release is not { } active || !billExists) return null;
        var page = rows.Take(request.Limit).ToArray();
        var next = rows.Count > request.Limit ? SponsorshipCursor.Of(active, page[^1]).Encode() : null;
        return new(active, page, next);
    }

    private static string ListSql(BillRequest request) => $"""
        SELECT a.release_number, {Columns}
          FROM (SELECT release_number FROM public_api.active_records LIMIT 1) a
          LEFT JOIN LATERAL (
               SELECT b.* FROM public_api.bills b
                WHERE TRUE{Filters(request)}{(request.Cursor is null ? "" : AfterCursor)}
                ORDER BY {Order} LIMIT @take) b
            ON TRUE
         ORDER BY {Order}
        """;

    private static string Filters(BillRequest request) =>
        (request.Congress is null ? "" : " AND b.congress = @congress") +
        (request.BillType is null ? "" : " AND b.bill_type = @bill_type") +
        (request.BillNumber is null ? "" : " AND b.bill_number = @bill_number") +
        (request.PolicyArea is null ? "" : " AND b.policy_area_id = @policy_area") +
        (request.Sponsor is null ? "" : """
             AND EXISTS (SELECT 1 FROM public_api.bill_sponsorships s
                          WHERE s.record_id = b.record_id AND s.revision_number = b.revision_number AND s.official_id = @sponsor)
            """) +
        (request.Issue is null ? "" : """
             AND EXISTS (SELECT 1 FROM public_api.bill_issues i
                          WHERE i.record_id = b.record_id AND i.revision_number = b.revision_number AND i.issue_id = @issue)
            """) +
        (request.From is null ? "" : " AND b.event_date >= @from") +
        (request.To is null ? "" : " AND b.event_date <= @to");

    private static void AddParameters(NpgsqlCommand command, BillRequest request)
    {
        var parameters = command.Parameters;
        parameters.AddWithValue("take", request.Limit + 1);
        if (request.Congress is { } congress) parameters.AddWithValue("congress", congress);
        if (request.BillType is not null) parameters.AddWithValue("bill_type", request.BillType);
        if (request.BillNumber is { } billNumber) parameters.AddWithValue("bill_number", billNumber);
        if (request.PolicyArea is not null) parameters.AddWithValue("policy_area", request.PolicyArea);
        if (request.Sponsor is not null) parameters.AddWithValue("sponsor", request.Sponsor);
        if (request.Issue is not null) parameters.AddWithValue("issue", request.Issue);
        if (request.From is { } from) parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.Date) { Value = from });
        if (request.To is { } to) parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.Date) { Value = to });
        if (request.Cursor is not { } cursor) return;
        parameters.Add(new NpgsqlParameter("cursor_date", NpgsqlDbType.Date) { Value = cursor.EventDate });
        parameters.AddWithValue("cursor_id", cursor.RecordId);
    }

    private static string SponsorshipSql(SponsorshipRequest request) => $"""
        SELECT a.release_number, EXISTS (SELECT 1 FROM public_api.bills b WHERE b.record_id = @id),
               s."position", s.official_id, s.role, s.is_original, s.sponsored_on, s.withdrawn_on
          FROM (SELECT release_number FROM public_api.active_records LIMIT 1) a
          LEFT JOIN LATERAL (
               SELECT s.* FROM public_api.bill_sponsorships s
                WHERE s.record_id = @id{(request.Role is null ? "" : " AND s.role = @role")}{(request.Cursor is null ? "" : " AND s.\"position\" > @cursor_position")}
                ORDER BY s."position" LIMIT @take) s
            ON TRUE
         ORDER BY s."position"
        """;

    // Column 0 holds the release. The row starts at column 1.
    private static BillRow ReadRow(DbDataReader reader) => new()
    {
        RecordId = reader.GetString(1),
        RevisionNumber = reader.GetInt32(2),
        Congress = reader.GetInt32(3),
        BillType = reader.GetString(4),
        BillNumber = reader.GetInt32(5),
        BillTitle = reader.GetString(6),
        Headline = reader.GetString(7),
        Summary = reader.GetString(8),
        TextOrigin = ViewColumns.Enum<PublishedTextOrigin>(reader, 9),
        Label = ViewColumns.Text(reader, 10),
        PolicyArea = ViewColumns.Text(reader, 11),
        PolicyAreaId = ViewColumns.Text(reader, 12),
        EventDate = reader.GetFieldValue<DateOnly>(13),
        SponsorCount = ViewColumns.Number(reader, 14)!.Value,
        CosponsorCount = ViewColumns.Number(reader, 15)!.Value
    };

    // Column 0 holds the release and column 1 holds whether the bill exists. The row starts at column 2.
    private static SponsorshipRow ReadSponsorship(DbDataReader reader) => new()
    {
        Position = reader.GetInt32(2),
        OfficialId = reader.GetString(3),
        Role = ViewColumns.Enum<PublishedSponsorshipRole>(reader, 4),
        IsOriginal = reader.GetBoolean(5),
        SponsoredOn = reader.GetFieldValue<DateOnly>(6),
        WithdrawnOn = reader.IsDBNull(7) ? null : reader.GetFieldValue<DateOnly>(7)
    };
}
