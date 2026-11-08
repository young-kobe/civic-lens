using System.Data;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Officials;
using CivicLens.Publication.Contracts;
using Npgsql;

namespace CivicLens.PublicApi.ZipDistricts;

/// <summary>Joins <c>public_api.zip_districts</c> with the name views of the active release. It is the one owner of the join from a ZCTA to the people who hold its seats.
/// A seat is held by a person whose term has begun and has not ended today.</summary>
public sealed class ZipDistrictStore(NpgsqlDataSource views)
{
    private const int SenateSeatsPerState = 2;

    private const string CurrentTerm = "t.start_on <= current_date AND (t.end_on IS NULL OR t.end_on >= current_date)";

    private const string DistrictsSql = $"""
        SELECT a.release_number, d.congress, d.state, d.district, h.official_id, h.name, h.party, h.end_on,
               EXISTS (SELECT 1 FROM public_api.release_officials) AS has_names
          FROM (SELECT release_number FROM public_api.active_records LIMIT 1) a
          LEFT JOIN LATERAL (
               SELECT z.congress, z.state, z.district, z.land_share FROM public_api.zip_districts z
                WHERE z.zcta = @zcta AND z.congress = COALESCE(@congress, (SELECT max(m.congress) FROM public_api.zip_districts m WHERE m.zcta = @zcta))) d
            ON TRUE
          LEFT JOIN LATERAL (
               SELECT t.official_id, o.name, t.party, t.end_on FROM public_api.release_official_terms t
                 JOIN public_api.release_officials o ON o.release_number = t.release_number AND o.official_id = t.official_id
                WHERE t.kind = 'Representative' AND t.state = d.state AND t.district = d.district AND {CurrentTerm}
                ORDER BY t.start_on DESC, t.official_id LIMIT 1) h
            ON TRUE
         ORDER BY d.land_share DESC, d.state, d.district
        """;

    private const string SenatorsSql = $"""
        SELECT state, official_id, name, party, end_on FROM (
            SELECT l.state, l.official_id, o.name, l.party, l.end_on, l.start_on,
                   row_number() OVER (PARTITION BY l.state ORDER BY (l.end_on IS NULL OR l.end_on > current_date) DESC, l.start_on DESC, l.official_id) AS newest
              FROM (SELECT DISTINCT ON (t.state, t.official_id) t.release_number, t.state, t.official_id, t.party, t.start_on, t.end_on
                      FROM public_api.release_official_terms t
                     WHERE t.kind = 'Senator' AND t.state = ANY(@states) AND {CurrentTerm}
                     ORDER BY t.state, t.official_id, t.start_on DESC, t."position" DESC) l
              JOIN public_api.release_officials o ON o.release_number = l.release_number AND o.official_id = l.official_id) ranked
         WHERE newest <= @seats
         ORDER BY state, start_on, official_id
        """;

    private const string HolderFiguresSql = $"""
        SELECT p.official_id, p.photo_path, a.chamber, a.congress, a.held, a.missed, a.first_voted_on, a.last_voted_on
          FROM public_api.officials p
          LEFT JOIN ({OfficialAttendanceReader.LatestSql}) a ON a.official_id = p.official_id
         WHERE p.official_id = ANY(@attendanceIds)
         ORDER BY p.official_id, a.chamber
        """;

    /// <summary>Finds the districts of a ZCTA. The lookup names the miss when no release is active, when the release holds no names, or when the ZCTA has no row for the Congress.
    /// One snapshot serves all reads, so the seats, portraits, and figures come from one release.</summary>
    public async Task<ZipLookup> FindAsync(ZipDistrictRequest request, CancellationToken cancellationToken)
    {
        await using var connection = await views.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var districts = await ReadDistrictsAsync(connection, request, cancellationToken);
        if (districts.Release is not { } release) return ZipLookup.NoRelease;
        if (!districts.HasNames) return ZipLookup.NoNames;
        if (districts.Rows.Count == 0) return ZipLookup.NotFound;
        var states = districts.Rows.Select(row => row.State).Distinct(StringComparer.Ordinal).ToArray();
        var senators = await ReadSenatorsAsync(connection, states, cancellationToken);
        var stateAnswers = states.Select(state => new ZipState(state, SenateSeats(state, senators))).ToArray();
        var holderIds = districts.Rows.Select(row => row.Representative?.OfficialId)
            .Concat(stateAnswers.SelectMany(state => state.Senators).Select(holder => holder?.OfficialId))
            .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        var figures = await ReadHolderFiguresAsync(connection, holderIds, cancellationToken);
        var partyLines = await ReadHolderPartyLinesAsync(connection, holderIds, figures, cancellationToken);
        var districtAnswers = districts.Rows.Select(row => new ZipDistrict(row.State, row.District, Enrich(row.Representative, figures, partyLines))).ToArray();
        var answer = new ZipDistrictAnswer(release, districts.Congress, request.Zcta,
            districtAnswers,
            [.. stateAnswers.Select(state => state with { Senators = [.. state.Senators.Select(holder => Enrich(holder, figures, partyLines))] })]);
        return ZipLookup.Of(answer);
    }

    private static SeatHolder?[] SenateSeats(string state, Dictionary<string, List<SeatHolder>> senators)
    {
        if (NonStateSeats.Contains(state)) return [];
        var holders = senators.GetValueOrDefault(state) ?? [];
        return [.. Enumerable.Range(0, SenateSeatsPerState).Select<int, SeatHolder?>(seat => seat < holders.Count ? holders[seat] : null)];
    }

    private static async Task<DistrictRows> ReadDistrictsAsync(NpgsqlConnection connection, ZipDistrictRequest request, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(DistrictsSql, connection);
        command.Parameters.AddWithValue("zcta", request.Zcta);
        command.Parameters.AddWithValue("congress", (object?)request.Congress ?? DBNull.Value);
        var rows = new List<DistrictRow>();
        int? release = null;
        var congress = 0;
        var hasNames = false;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            release = reader.GetInt32(0);
            hasNames = reader.GetBoolean(8);
            if (reader.IsDBNull(1)) continue;
            congress = reader.GetInt32(1);
            rows.Add(new(reader.GetString(2), reader.GetInt32(3), ReadHolder(reader, 4)));
        }
        return new(release, hasNames, congress, rows);
    }

    private static async Task<Dictionary<string, List<SeatHolder>>> ReadSenatorsAsync(NpgsqlConnection connection, string[] states, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(SenatorsSql, connection);
        command.Parameters.AddWithValue("states", states);
        command.Parameters.AddWithValue("seats", SenateSeatsPerState);
        var senators = new Dictionary<string, List<SeatHolder>>(StringComparer.Ordinal);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var state = reader.GetString(0);
            if (!senators.TryGetValue(state, out var holders)) senators[state] = holders = [];
            holders.Add(ReadHolder(reader, 1)!);
        }
        return senators;
    }

    private static SeatHolder? ReadHolder(NpgsqlDataReader reader, int first) =>
        reader.IsDBNull(first) ? null : new(reader.GetString(first), reader.GetString(first + 1), ViewColumns.Text(reader, first + 2), ViewColumns.Date(reader, first + 3), null, [], []);

    private static SeatHolder? Enrich(SeatHolder? holder, Dictionary<string, HolderFigures> figures, Dictionary<string, List<PartyLineFigure>> partyLines) =>
        holder is null ? null : holder with
        {
            PhotoPath = figures[holder.OfficialId].PhotoPath,
            MissedVotes = [.. figures[holder.OfficialId].MissedVotes],
            PartyLines = [.. partyLines.GetValueOrDefault(holder.OfficialId) ?? []]
        };

    private static async Task<Dictionary<string, HolderFigures>> ReadHolderFiguresAsync(NpgsqlConnection connection, string[] officialIds, CancellationToken cancellationToken)
    {
        var figures = new Dictionary<string, HolderFigures>(StringComparer.Ordinal);
        if (officialIds.Length == 0) return figures;
        await using var command = new NpgsqlCommand(HolderFiguresSql, connection);
        command.Parameters.AddWithValue("attendanceIds", officialIds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            if (!figures.TryGetValue(id, out var holder))
                figures[id] = holder = new(ViewColumns.Text(reader, 1), reader.IsDBNull(3) ? null : reader.GetInt32(3));
            if (!reader.IsDBNull(2)) holder.MissedVotes.Add(OfficialAttendanceReader.ReadFigure(reader, 2));
        }
        return figures;
    }

    private static async Task<Dictionary<string, List<PartyLineFigure>>> ReadHolderPartyLinesAsync(NpgsqlConnection connection, string[] officialIds,
        Dictionary<string, HolderFigures> holderFigures, CancellationToken cancellationToken)
    {
        var partyLines = new Dictionary<string, List<PartyLineFigure>>(StringComparer.Ordinal);
        var withAttendance = officialIds.Where(id => holderFigures.GetValueOrDefault(id)?.AttendanceCongress is not null).ToArray();
        if (withAttendance.Length == 0) return partyLines;
        await using var command = new NpgsqlCommand(OfficialPartyLineReader.ForAttendanceSql, connection);
        command.Parameters.AddWithValue("officialIds", withAttendance);
        command.Parameters.AddWithValue("congresses", withAttendance.Select(id => holderFigures[id].AttendanceCongress!.Value).ToArray());
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            if (!partyLines.TryGetValue(id, out var rows)) partyLines[id] = rows = [];
            rows.Add(OfficialPartyLineReader.ReadFigure(reader, 1));
        }
        return partyLines;
    }

    private sealed record DistrictRow(string State, int District, SeatHolder? Representative);

    private sealed record DistrictRows(int? Release, bool HasNames, int Congress, List<DistrictRow> Rows);

    private sealed class HolderFigures(string? photoPath, int? attendanceCongress)
    {
        public string? PhotoPath { get; } = photoPath;
        public int? AttendanceCongress { get; } = attendanceCongress;
        public List<MissedVotesFigure> MissedVotes { get; } = [];
    }
}
