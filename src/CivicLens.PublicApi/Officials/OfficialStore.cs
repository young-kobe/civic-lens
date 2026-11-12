using System.Data;
using System.Data.Common;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Npgsql;

namespace CivicLens.PublicApi.Officials;

/// <summary>
/// Reads the views <c>public_api.officials</c>, <c>official_attendance</c>, <c>official_party_lines</c>, <c>official_issue_counts</c>, and <c>official_executive_actions</c>,
/// and the name view <c>release_official_terms</c>. The kind rule of the vote counts is <see cref="PublishedVoteKindExtensions.IsSubstantive"/>.
/// </summary>
public sealed class OfficialStore(NpgsqlDataSource views)
{
    private const string Columns = "o.official_id, o.name, o.kind, o.office, o.state, o.district, o.party, o.caucus, o.term_end_on, o.record_count, o.photo_path";
    private const string Order = "o.name, o.official_id";

    private const string DetailSql = $"""
        SELECT a.release_number, a.published_at_utc_ticks, {Columns}
          FROM (SELECT release_number, published_at_utc_ticks FROM public_api.active_release LIMIT 1) a
          LEFT JOIN LATERAL (SELECT o.* FROM public_api.officials o WHERE o.official_id = @id) o ON TRUE
        """;

    private const string TermsSql = """
        SELECT office, kind, state, district, party, caucus, start_on, end_on
          FROM public_api.release_official_terms WHERE official_id = @id ORDER BY "position"
        """;

    private const string AttendanceSql = $"""
        SELECT chamber, congress, held, missed, first_voted_on, last_voted_on
          FROM ({OfficialAttendanceReader.LatestSql}) latest ORDER BY chamber
        """;

    private const string PartyLinesSql = """
        SELECT chamber, congress, party, with_party, against_party, cast_count, first_voted_on, last_voted_on
          FROM public_api.official_party_lines WHERE official_id = @id AND congress = @congress ORDER BY chamber, party
        """;

    private const string ExecutiveActionsSql = """
        SELECT document_type, actions FROM public_api.official_executive_actions WHERE official_id = @id ORDER BY document_type
        """;

    private const string ExecutiveActionBaseSql = "SELECT count(*) FROM public_api.active_records WHERE kind = 'executiveAction'";

    /// <summary>Reads one page. Null means that no release is active. The caller compares the release of the page with the release of the cursor.</summary>
    public async Task<OfficialPage?> ListAsync(OfficialRequest request, CancellationToken cancellationToken)
    {
        await using var command = views.CreateCommand(ListSql(request));
        AddParameters(command, request);
        var rows = new List<OfficialRow>();
        int? release = null;
        var listed = 0;
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                release = reader.GetInt32(0);
                listed = reader.GetInt32(1);
                if (!reader.IsDBNull(2)) rows.Add(ReadRow(reader, 2));
            }
        if (release is not { } active) return null;
        var page = rows.Take(request.Limit).ToArray();
        var next = rows.Count > request.Limit ? OfficialCursor.Of(active, page[^1]).Encode() : null;
        return new(active, OfficialScopes.RecordCount, listed, OfficialScopes.OfficialsInRelease, page, next);
    }

    /// <summary>Reads one official. Null means that no release is active or that the active release names no such official.
    /// One snapshot serves every read, so the figures come from one release.</summary>
    public async Task<OfficialDetail?> FindAsync(string officialId, CancellationToken cancellationToken)
    {
        await using var connection = await views.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        return (await ReadDetailAsync(connection, transaction, officialId, cancellationToken))?.Detail;
    }

    /// <summary>Reads one official and the publish time of the release inside the caller's snapshot, so a page that reads more views shares the release of its figures.</summary>
    internal static async Task<(OfficialDetail Detail, long? PublishedAtUtcTicks)?> ReadDetailAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string officialId, CancellationToken cancellationToken)
    {
        var found = await ReadOfficialAsync(connection, transaction, officialId, cancellationToken);
        if (found is not var (release, publishedAtUtcTicks, official)) return null;
        var terms = await ReadTermsAsync(connection, transaction, officialId, cancellationToken);
        var attendance = await ReadAttendanceAsync(connection, transaction, officialId, cancellationToken);
        var congress = attendance.Count == 0 ? (int?)null : attendance[0].Congress;
        var missed = attendance.ToArray();
        var partyLines = congress is { } partyCongress ? await ReadPartyLinesAsync(connection, transaction, officialId, partyCongress, cancellationToken) : [];
        var activityCongress = OfficialActivityReader.CongressForActivity(official.Kind, congress, publishedAtUtcTicks);
        var activity = activityCongress is { } current
            ? (await OfficialActivityReader.ReadAsync(connection, transaction, [(officialId, current)], cancellationToken))[(officialId, current)]
            : null;
        var bills = activity?.Bills;
        var issues = activity?.Issues;
        var executive = official.Kind == PublishedOfficeKind.Executive ? await ReadExecutiveActionsAsync(connection, transaction, officialId, cancellationToken) : null;
        return (new(release, official, OfficialScopes.RecordCount, terms, congress, missed, partyLines, bills, issues, executive), publishedAtUtcTicks);
    }

    // The view is read once. The CTE feeds both the count of the whole release and the filtered page.
    private static string ListSql(OfficialRequest request) => $"""
        WITH listed AS MATERIALIZED (SELECT * FROM public_api.officials)
        SELECT a.release_number, (SELECT count(*) FROM listed) AS officials_in_release, {Columns}
          FROM (SELECT release_number FROM public_api.active_release LIMIT 1) a
          LEFT JOIN LATERAL (
               SELECT o.* FROM listed o
                WHERE TRUE{Filters(request)}
                ORDER BY {Order} LIMIT @take) o
            ON TRUE
         ORDER BY {Order}
        """;

    private static string Filters(OfficialRequest request) =>
        (request.Kind is null ? "" : " AND o.kind = @kind") +
        (request.State is null ? "" : " AND o.state = @state") +
        (request.Party is null ? "" : " AND lower(o.party) = @party") +
        (request.NamePrefix is null ? "" : " AND strpos(' ' || lower(o.name), ' ' || @name_prefix) > 0") +
        (request.Cursor is null ? "" : " AND (o.name, o.official_id) > (@cursor_name, @cursor_id)");

    private static void AddParameters(NpgsqlCommand command, OfficialRequest request)
    {
        var parameters = command.Parameters;
        parameters.AddWithValue("take", request.Limit + 1);
        if (request.Kind is { } kind) parameters.AddWithValue("kind", kind.ToString());
        if (request.State is not null) parameters.AddWithValue("state", request.State);
        if (request.Party is not null) parameters.AddWithValue("party", request.Party);
        if (request.NamePrefix is not null) parameters.AddWithValue("name_prefix", request.NamePrefix);
        if (request.Cursor is not { } cursor) return;
        parameters.AddWithValue("cursor_name", cursor.Name);
        parameters.AddWithValue("cursor_id", cursor.OfficialId);
    }

    private static async Task<(int Release, long? PublishedAtUtcTicks, OfficialRow Official)?> ReadOfficialAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string officialId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(DetailSql, connection);
        command.Transaction = transaction;
        command.Parameters.AddWithValue("id", officialId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) && !reader.IsDBNull(2)
            ? (reader.GetInt32(0), reader.IsDBNull(1) ? (long?)null : reader.GetInt64(1), ReadRow(reader, 2))
            : null;
    }

    private static async Task<OfficialTerm[]> ReadTermsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string officialId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(TermsSql, connection);
        command.Transaction = transaction;
        command.Parameters.AddWithValue("id", officialId);
        var terms = new List<OfficialTerm>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            terms.Add(new(reader.GetString(0), ViewColumns.Enum<PublishedOfficeKind>(reader, 1), ViewColumns.Text(reader, 2), ViewColumns.Number(reader, 3),
                ViewColumns.Text(reader, 4), ViewColumns.Text(reader, 5), reader.GetFieldValue<DateOnly>(6), ViewColumns.Date(reader, 7)));
        return [.. terms];
    }

    private static async Task<List<MissedVotesFigure>> ReadAttendanceAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string officialId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(AttendanceSql, connection);
        command.Transaction = transaction;
        command.Parameters.AddWithValue("attendanceIds", new[] { officialId });
        var figures = new List<MissedVotesFigure>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            figures.Add(OfficialAttendanceReader.ReadFigure(reader, 0));
        }
        return figures;
    }

    private static async Task<PartyLineFigure[]> ReadPartyLinesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string officialId, int congress, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(PartyLinesSql, connection);
        command.Transaction = transaction;
        command.Parameters.AddWithValue("id", officialId);
        command.Parameters.AddWithValue("congress", congress);
        var figures = new List<PartyLineFigure>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            figures.Add(OfficialPartyLineReader.ReadFigure(reader, 0));
        }
        return [.. figures];
    }

    private static async Task<ExecutiveActionsFigure> ReadExecutiveActionsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string officialId, CancellationToken cancellationToken)
    {
        var counts = new List<ExecutiveActionCount>();
        await using (var command = new NpgsqlCommand(ExecutiveActionsSql, connection))
        {
            command.Transaction = transaction;
            command.Parameters.AddWithValue("id", officialId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                counts.Add(new(reader.GetString(0), ViewColumns.Number(reader, 1)!.Value));
        }
        await using var baseCommand = new NpgsqlCommand(ExecutiveActionBaseSql, connection);
        baseCommand.Transaction = transaction;
        var total = Convert.ToInt32(await baseCommand.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        return new(OfficialScopes.ExecutiveActions, total, [.. counts]);
    }

    private static OfficialRow ReadRow(DbDataReader reader, int firstColumn) => new()
    {
        OfficialId = reader.GetString(firstColumn),
        Name = reader.GetString(firstColumn + 1),
        Kind = reader.IsDBNull(firstColumn + 2) ? null : ViewColumns.Enum<PublishedOfficeKind>(reader, firstColumn + 2),
        Office = ViewColumns.Text(reader, firstColumn + 3),
        State = ViewColumns.Text(reader, firstColumn + 4),
        District = ViewColumns.Number(reader, firstColumn + 5),
        Party = ViewColumns.Text(reader, firstColumn + 6),
        Caucus = ViewColumns.Text(reader, firstColumn + 7),
        TermEndsOn = ViewColumns.Date(reader, firstColumn + 8),
        RecordCount = ViewColumns.Number(reader, firstColumn + 9)!.Value,
        PhotoPath = ViewColumns.Text(reader, firstColumn + 10)
    };
}
