using System.Data;
using System.Data.Common;
using CivicLens.Publication.Contracts;
using CivicLens.PublicApi.Http;
using Npgsql;

namespace CivicLens.PublicApi.Officials;

/// <summary>
/// Reads the views <c>public_api.officials</c>, <c>official_attendance</c>, <c>official_party_lines</c>, <c>official_bill_counts</c>, <c>official_issue_counts</c>, and <c>official_executive_actions</c>,
/// and the name view <c>release_official_terms</c>. The kind rule of the vote counts is <see cref="PublishedVoteKindExtensions.IsSubstantive"/>.
/// </summary>
public sealed class OfficialStore(NpgsqlDataSource views)
{
    /// <summary>The most issues in one official answer.</summary>
    public const int MaximumIssues = 10;

    private static readonly string[] SubstantiveKinds = [.. Enum.GetValues<PublishedVoteKind>().Where(kind => kind.IsSubstantive()).Select(kind => kind.ToString())];

    private const string Columns = "o.official_id, o.name, o.kind, o.office, o.state, o.district, o.party, o.caucus, o.term_end_on, o.record_count, o.photo_path";
    private const string Order = "o.name, o.official_id";

    private const string DetailSql = $"""
        SELECT a.release_number, {Columns}
          FROM (SELECT release_number FROM public_api.active_records LIMIT 1) a
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

    private const string BillsSql = """
        SELECT coalesce(c.sponsored, 0), coalesce(c.cosponsored, 0), (SELECT count(*) FROM public_api.bills WHERE congress = @congress)
          FROM (SELECT 1) one
          LEFT JOIN public_api.official_bill_counts c ON c.official_id = @id AND c.congress = @congress
        """;

    private const string IssuesSql = """
        SELECT issue_id, coalesce(sum(records) FILTER (WHERE source = 'bill'), 0) AS bills,
               coalesce(sum(records) FILTER (WHERE source = 'vote' AND vote_kind = ANY(@kinds)), 0) AS votes
          FROM public_api.official_issue_counts WHERE official_id = @id AND congress = @congress
         GROUP BY issue_id
        HAVING sum(records) FILTER (WHERE source = 'bill' OR vote_kind = ANY(@kinds)) > 0
         ORDER BY coalesce(sum(records) FILTER (WHERE source = 'bill'), 0) + coalesce(sum(records) FILTER (WHERE source = 'vote' AND vote_kind = ANY(@kinds)), 0) DESC, issue_id
         LIMIT @take
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
        await using (var reader = await command.ExecuteReaderAsync(cancellationToken))
            while (await reader.ReadAsync(cancellationToken))
            {
                release = reader.GetInt32(0);
                if (!reader.IsDBNull(1)) rows.Add(ReadRow(reader));
            }
        if (release is not { } active) return null;
        var page = rows.Take(request.Limit).ToArray();
        var next = rows.Count > request.Limit ? OfficialCursor.Of(active, page[^1]).Encode() : null;
        return new(active, OfficialScopes.RecordCount, page, next);
    }

    /// <summary>Reads one official. Null means that no release is active or that the active release names no such official.
    /// One snapshot serves every read, so the figures come from one release.</summary>
    public async Task<OfficialDetail?> FindAsync(string officialId, CancellationToken cancellationToken)
    {
        await using var connection = await views.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var found = await ReadOfficialAsync(connection, officialId, cancellationToken);
        if (found is not var (release, official)) return null;
        var terms = await ReadTermsAsync(connection, officialId, cancellationToken);
        var attendance = await ReadAttendanceAsync(connection, officialId, cancellationToken);
        var congress = attendance.Count == 0 ? (int?)null : attendance[0].Congress;
        var missed = attendance.ToArray();
        var partyLines = congress is { } partyCongress ? await ReadPartyLinesAsync(connection, officialId, partyCongress, cancellationToken) : [];
        var bills = congress is { } current ? await ReadBillsAsync(connection, officialId, current, cancellationToken) : null;
        var issues = congress is { } newest ? await ReadIssuesAsync(connection, officialId, newest, cancellationToken) : null;
        var executive = official.Kind == PublishedOfficeKind.Executive ? await ReadExecutiveActionsAsync(connection, officialId, cancellationToken) : null;
        return new(release, official, OfficialScopes.RecordCount, terms, congress, missed, partyLines, bills, issues, executive);
    }

    private static string ListSql(OfficialRequest request) => $"""
        SELECT a.release_number, {Columns}
          FROM (SELECT release_number FROM public_api.active_records LIMIT 1) a
          LEFT JOIN LATERAL (
               SELECT o.* FROM public_api.officials o
                WHERE TRUE{Filters(request)}
                ORDER BY {Order} LIMIT @take) o
            ON TRUE
         ORDER BY {Order}
        """;

    private static string Filters(OfficialRequest request) =>
        (request.Kind is null ? "" : " AND o.kind = @kind") +
        (request.State is null ? "" : " AND o.state = @state") +
        (request.Party is null ? "" : " AND lower(o.party) = @party") +
        (request.NamePrefix is null ? "" : " AND starts_with(lower(o.name), @name_prefix)") +
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

    private async Task<(int Release, OfficialRow Official)?> ReadOfficialAsync(NpgsqlConnection connection, string officialId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(DetailSql, connection);
        command.Parameters.AddWithValue("id", officialId);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        return await reader.ReadAsync(cancellationToken) && !reader.IsDBNull(1) ? (reader.GetInt32(0), ReadRow(reader)) : null;
    }

    private static async Task<OfficialTerm[]> ReadTermsAsync(NpgsqlConnection connection, string officialId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(TermsSql, connection);
        command.Parameters.AddWithValue("id", officialId);
        var terms = new List<OfficialTerm>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            terms.Add(new(reader.GetString(0), ViewColumns.Enum<PublishedOfficeKind>(reader, 1), ViewColumns.Text(reader, 2), ViewColumns.Number(reader, 3),
                ViewColumns.Text(reader, 4), ViewColumns.Text(reader, 5), reader.GetFieldValue<DateOnly>(6), ViewColumns.Date(reader, 7)));
        return [.. terms];
    }

    private static async Task<List<MissedVotesFigure>> ReadAttendanceAsync(NpgsqlConnection connection, string officialId, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(AttendanceSql, connection);
        command.Parameters.AddWithValue("attendanceIds", new[] { officialId });
        var figures = new List<MissedVotesFigure>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            figures.Add(OfficialAttendanceReader.ReadFigure(reader, 0));
        }
        return figures;
    }

    private static async Task<PartyLineFigure[]> ReadPartyLinesAsync(NpgsqlConnection connection, string officialId, int congress, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(PartyLinesSql, connection);
        command.Parameters.AddWithValue("id", officialId);
        command.Parameters.AddWithValue("congress", congress);
        var figures = new List<PartyLineFigure>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var chamber = ViewColumns.Enum<PublishedChamber>(reader, 0);
            var party = reader.GetString(2);
            figures.Add(new(chamber, reader.GetInt32(1), party, ViewColumns.Number(reader, 3)!.Value, ViewColumns.Number(reader, 4)!.Value, ViewColumns.Number(reader, 5)!.Value,
                reader.GetFieldValue<DateOnly>(6), reader.GetFieldValue<DateOnly>(7), OfficialScopes.PartyLine(chamber, congress, party)));
        }
        return [.. figures];
    }

    private static async Task<BillCountsFigure> ReadBillsAsync(NpgsqlConnection connection, string officialId, int congress, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(BillsSql, connection);
        command.Parameters.AddWithValue("id", officialId);
        command.Parameters.AddWithValue("congress", congress);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);
        return new(congress, ViewColumns.Number(reader, 0)!.Value, ViewColumns.Number(reader, 1)!.Value, ViewColumns.Number(reader, 2)!.Value, OfficialScopes.Bills(congress));
    }

    private static async Task<IssueCountsFigures> ReadIssuesAsync(NpgsqlConnection connection, string officialId, int congress, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(IssuesSql, connection);
        command.Parameters.AddWithValue("id", officialId);
        command.Parameters.AddWithValue("congress", congress);
        command.Parameters.AddWithValue("kinds", SubstantiveKinds);
        command.Parameters.AddWithValue("take", MaximumIssues);
        var issues = new List<IssueCountsFigure>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
            issues.Add(new(reader.GetString(0), ViewColumns.Number(reader, 1)!.Value, ViewColumns.Number(reader, 2)!.Value));
        return new(congress, OfficialScopes.Issues(congress), [.. issues]);
    }

    private static async Task<ExecutiveActionsFigure> ReadExecutiveActionsAsync(NpgsqlConnection connection, string officialId, CancellationToken cancellationToken)
    {
        var counts = new List<ExecutiveActionCount>();
        await using (var command = new NpgsqlCommand(ExecutiveActionsSql, connection))
        {
            command.Parameters.AddWithValue("id", officialId);
            await using var reader = await command.ExecuteReaderAsync(cancellationToken);
            while (await reader.ReadAsync(cancellationToken))
                counts.Add(new(reader.GetString(0), ViewColumns.Number(reader, 1)!.Value));
        }
        await using var baseCommand = new NpgsqlCommand(ExecutiveActionBaseSql, connection);
        var total = Convert.ToInt32(await baseCommand.ExecuteScalarAsync(cancellationToken), System.Globalization.CultureInfo.InvariantCulture);
        return new(OfficialScopes.ExecutiveActions, total, [.. counts]);
    }

    // Column 0 holds the release. The row starts at column 1.
    private static OfficialRow ReadRow(DbDataReader reader) => new()
    {
        OfficialId = reader.GetString(1),
        Name = reader.GetString(2),
        Kind = reader.IsDBNull(3) ? null : ViewColumns.Enum<PublishedOfficeKind>(reader, 3),
        Office = ViewColumns.Text(reader, 4),
        State = ViewColumns.Text(reader, 5),
        District = ViewColumns.Number(reader, 6),
        Party = ViewColumns.Text(reader, 7),
        Caucus = ViewColumns.Text(reader, 8),
        TermEndsOn = ViewColumns.Date(reader, 9),
        RecordCount = ViewColumns.Number(reader, 10)!.Value,
        PhotoPath = ViewColumns.Text(reader, 11)
    };
}
