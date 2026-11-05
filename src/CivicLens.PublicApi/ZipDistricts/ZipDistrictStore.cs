using System.Data;
using CivicLens.PublicApi.Http;
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

    /// <summary>Finds the districts of a ZCTA. The lookup names the miss when no release is active, when the release holds no names, or when the ZCTA has no row for the Congress.
    /// One snapshot serves both reads, so the districts and the senators come from one release.</summary>
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
        var answer = new ZipDistrictAnswer(release, districts.Congress, request.Zcta,
            [.. districts.Rows.Select(row => new ZipDistrict(row.State, row.District, row.Representative))],
            [.. states.Select(state => new ZipState(state, SenateSeats(state, senators)))]);
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
        reader.IsDBNull(first) ? null : new(reader.GetString(first), reader.GetString(first + 1), ViewColumns.Text(reader, first + 2), NullableDate(reader, first + 3));

    private static DateOnly? NullableDate(NpgsqlDataReader reader, int ordinal) =>
        reader.IsDBNull(ordinal) ? null : reader.GetFieldValue<DateOnly>(ordinal);

    private sealed record DistrictRow(string State, int District, SeatHolder? Representative);

    private sealed record DistrictRows(int? Release, bool HasNames, int Congress, List<DistrictRow> Rows);
}
