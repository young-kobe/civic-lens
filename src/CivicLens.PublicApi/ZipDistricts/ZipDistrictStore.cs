using CivicLens.PublicApi.Issues;
using System.Data;
using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Officials;
using CivicLens.PublicApi.Votes;
using CivicLens.Publication.Contracts;
using Npgsql;

namespace CivicLens.PublicApi.ZipDistricts;

/// <summary>Joins <c>public_api.zip_districts</c> with the name views of the active release. It is the one owner of the join from a ZCTA to the people who hold its seats.
/// A seat is held by a person whose term has begun and has not ended today.</summary>
public sealed class ZipDistrictStore(NpgsqlDataSource views)
{
    private const int SenateSeatsPerState = 2;

    private const string IssueCongressSql = "SELECT count_congress FROM public_api.release_issues WHERE issue_id = @issue";

    private const string CurrentTerm = "t.start_on <= current_date AND (t.end_on IS NULL OR t.end_on >= current_date)";

    private const string DistrictsSql = $"""
        SELECT a.release_number, a.published_at_utc_ticks, d.congress, d.state, d.district, h.official_id, h.name, h.party, h.end_on,
               EXISTS (SELECT 1 FROM public_api.release_officials) AS has_names
          FROM (SELECT release_number, published_at_utc_ticks FROM public_api.active_release LIMIT 1) a
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

    private const string HolderPartyLinesSql = """
        SELECT lines.official_id, lines.chamber, lines.congress, lines.party, lines.with_party, lines.against_party,
               lines.cast_count, lines.first_voted_on, lines.last_voted_on
          FROM unnest(@officialIds, @congresses) AS latest(official_id, congress)
          JOIN public_api.official_party_lines lines
            ON lines.official_id = latest.official_id AND lines.congress = latest.congress
         WHERE lines.official_id = ANY(@officialIds)
         ORDER BY lines.official_id, lines.chamber, lines.party
        """;

    private const string NewVoteCountSql = """
        SELECT count(*)
          FROM public_api.vote_first_publications p
         WHERE p.release_number = @release AND p.first_release > @sinceRelease
           AND EXISTS (
               SELECT 1 FROM public_api.vote_members m
                WHERE m.record_id = p.record_id AND m.revision_number = p.revision_number
                  AND m.official_id = ANY(@officialIds))
        """;

    /// <summary>Finds the districts of a ZCTA. The lookup names the miss when no release is active, when the release holds no names, or when the ZCTA has no row for the Congress.
    /// One snapshot serves all reads, so the seats, portraits, and figures come from one release.</summary>
    public Task<ZipLookup> FindAsync(ZipDistrictRequest request, CancellationToken cancellationToken) => FindCoreAsync(request, LookupShape.Figures, null, null, cancellationToken);

    /// <summary>Finds district members and shared votes, with an optional count of vote records first published after a release.</summary>
    public Task<ZipLookup> FindMembersAsync(ZipDistrictRequest request, string? issue, int? sinceRelease, CancellationToken cancellationToken) =>
        FindCoreAsync(request, LookupShape.Members, issue, sinceRelease, cancellationToken);

    /// <summary>Finds current district members without loading their attendance, party, activity, or shared-vote details.</summary>
    public Task<ZipLookup> FindStripAsync(ZipDistrictRequest request, int? sinceRelease, CancellationToken cancellationToken) =>
        FindCoreAsync(request, LookupShape.Strip, null, sinceRelease, cancellationToken);

    /// <summary>Finds current district members and the shared votes of one issue without loading their figures, activity, or issue options.</summary>
    public Task<ZipLookup> FindIssueVotesAsync(ZipDistrictRequest request, string issue, CancellationToken cancellationToken) =>
        FindCoreAsync(request, LookupShape.Issue, issue, null, cancellationToken);

    private async Task<ZipLookup> FindCoreAsync(ZipDistrictRequest request, LookupShape shape, string? issue, int? sinceRelease, CancellationToken cancellationToken)
    {
        await using var connection = await views.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var districts = await ReadDistrictsAsync(connection, transaction, request, cancellationToken);
        if (districts.Release is not { } release) return ZipLookup.NoRelease;
        if (!districts.HasNames) return ZipLookup.NoNames;
        if (districts.Rows.Count == 0) return ZipLookup.NotFound;
        var states = districts.Rows.Select(row => row.State).Distinct(StringComparer.Ordinal).ToArray();
        var senators = await ReadSenatorsAsync(connection, transaction, states, cancellationToken);
        var stateAnswers = states.Select(state => new ZipState(state, SenateSeats(state, senators))).ToArray();
        var holderIds = districts.Rows.Select(row => row.Representative?.OfficialId)
            .Concat(stateAnswers.SelectMany(state => state.Senators).Select(holder => holder?.OfficialId))
            .OfType<string>().Distinct(StringComparer.Ordinal).ToArray();
        var newVoteCount = await ReadNewVoteCountAsync(connection, transaction, release, holderIds, sinceRelease, cancellationToken);
        if (shape is LookupShape.Strip or LookupShape.Issue)
        {
            var bareAnswer = new ZipDistrictAnswer(release, districts.Congress, request.Zcta,
                [.. districts.Rows.Select(row => new ZipDistrict(row.State, row.District, row.Representative))],
                [.. stateAnswers]);
            var bare = ZipLookup.Of(bareAnswer) with { NewVoteCount = newVoteCount };
            return shape == LookupShape.Issue ? await WithSharedVotesAsync(connection, transaction, bare, shape, holderIds, issue, cancellationToken) : bare;
        }
        var figures = await ReadHolderFiguresAsync(connection, transaction, holderIds, cancellationToken);
        var partyLines = await ReadHolderPartyLinesAsync(connection, transaction, holderIds, figures, cancellationToken);
        var activitySelections = figures.Select(pair => (OfficialId: pair.Key,
                Congress: OfficialActivityReader.CongressForActivity(PublishedOfficeKind.Representative, pair.Value.AttendanceCongress, districts.PublishedAtUtcTicks)))
            .Where(selection => selection.Congress is not null)
            .Select(selection => (selection.OfficialId, selection.Congress!.Value)).ToArray();
        var activityCongressByOfficial = activitySelections.ToDictionary(selection => selection.OfficialId, selection => selection.Value, StringComparer.Ordinal);
        var activity = await OfficialActivityReader.ReadAsync(connection, transaction, activitySelections, cancellationToken);
        var districtAnswers = districts.Rows.Select(row => new ZipDistrict(row.State, row.District, Enrich(row.Representative, figures, partyLines, activityCongressByOfficial, activity))).ToArray();
        var answer = new ZipDistrictAnswer(release, districts.Congress, request.Zcta,
            districtAnswers,
            [.. stateAnswers.Select(state => state with { Senators = [.. state.Senators.Select(holder => Enrich(holder, figures, partyLines, activityCongressByOfficial, activity))] })]);
        var lookup = ZipLookup.Of(answer);
        lookup = lookup with { NewVoteCount = newVoteCount };
        if (shape == LookupShape.Figures) return lookup;
        var issueOptions = await IssueStore.ListOptionsAsync(connection, transaction, cancellationToken);
        lookup = lookup with { IssueOptions = issueOptions };
        return await WithSharedVotesAsync(connection, transaction, lookup, shape, holderIds, issue, cancellationToken);
    }

    private static async Task<ZipLookup> WithSharedVotesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ZipLookup lookup,
        LookupShape shape, string[] holderIds, string? issue, CancellationToken cancellationToken)
    {
        if (holderIds.Length == 0) return lookup;
        var congress = shape == LookupShape.Issue ? await ReadIssueCongressAsync(connection, transaction, issue!, cancellationToken) : null;
        var sharedVotes = await MemberVoteStore.ReadSharedAsync(connection, transaction,
            new MemberVoteRequest { Officials = holderIds, Issue = issue, Congress = congress }, cancellationToken);
        if (sharedVotes is null || sharedVotes.Release != lookup.Answer!.Release)
            throw new InvalidDataException("The ZIP member votes do not match the active release snapshot.");
        return lookup with { SharedVotes = sharedVotes };
    }

    /// <summary>The Congress that the issue page counts for the issue, so the members section uses the same scope. Null when the release has no count Congress for the issue.</summary>
    private static async Task<int?> ReadIssueCongressAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string issue, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(IssueCongressSql, connection, transaction);
        command.Parameters.AddWithValue("issue", issue);
        return await command.ExecuteScalarAsync(cancellationToken) is int congress ? congress : null;
    }

    private static async Task<long?> ReadNewVoteCountAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, int release,
        string[] officialIds, int? sinceRelease, CancellationToken cancellationToken)
    {
        if (sinceRelease is null) return null;
        if (officialIds.Length == 0 || sinceRelease.Value >= release) return 0;
        await using var command = new NpgsqlCommand(NewVoteCountSql, connection, transaction);
        command.Parameters.AddWithValue("release", release);
        command.Parameters.AddWithValue("sinceRelease", sinceRelease.Value);
        command.Parameters.AddWithValue("officialIds", officialIds);
        return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
    }

    private static SeatHolder?[] SenateSeats(string state, Dictionary<string, List<SeatHolder>> senators)
    {
        if (NonStateSeats.Contains(state)) return [];
        var holders = senators.GetValueOrDefault(state) ?? [];
        return [.. Enumerable.Range(0, SenateSeatsPerState).Select<int, SeatHolder?>(seat => seat < holders.Count ? holders[seat] : null)];
    }

    private static async Task<DistrictRows> ReadDistrictsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, ZipDistrictRequest request, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(DistrictsSql, connection);
        command.Transaction = transaction;
        command.Parameters.AddWithValue("zcta", request.Zcta);
        command.Parameters.AddWithValue("congress", (object?)request.Congress ?? DBNull.Value);
        var rows = new List<DistrictRow>();
        int? release = null;
        long? publishedAtUtcTicks = null;
        var congress = 0;
        var hasNames = false;
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            release = reader.GetInt32(0);
            publishedAtUtcTicks = reader.IsDBNull(1) ? null : reader.GetInt64(1);
            hasNames = reader.GetBoolean(9);
            if (reader.IsDBNull(2)) continue;
            congress = reader.GetInt32(2);
            rows.Add(new(reader.GetString(3), reader.GetInt32(4), ReadHolder(reader, 5)));
        }
        return new(release, publishedAtUtcTicks, hasNames, congress, rows);
    }

    private static async Task<Dictionary<string, List<SeatHolder>>> ReadSenatorsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string[] states, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(SenatorsSql, connection);
        command.Transaction = transaction;
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

    private static SeatHolder? Enrich(SeatHolder? holder, Dictionary<string, HolderFigures> figures,
        Dictionary<string, List<PartyLineFigure>> partyLines,
        Dictionary<string, int> activityCongressByOfficial,
        Dictionary<(string OfficialId, int Congress), OfficialActivityFigures> activity)
    {
        if (holder is null) return null;
        var holderFigures = figures[holder.OfficialId];
        var holderActivity = activityCongressByOfficial.TryGetValue(holder.OfficialId, out var congress)
            ? activity.GetValueOrDefault((holder.OfficialId, congress))
            : null;
        return holder with
        {
            PhotoPath = holderFigures.PhotoPath,
            MissedVotes = [.. holderFigures.MissedVotes],
            PartyLines = [.. partyLines.GetValueOrDefault(holder.OfficialId) ?? []],
            Bills = holderActivity?.Bills,
            Issues = holderActivity?.Issues
        };
    }

    private static async Task<Dictionary<string, HolderFigures>> ReadHolderFiguresAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string[] officialIds, CancellationToken cancellationToken)
    {
        var figures = new Dictionary<string, HolderFigures>(StringComparer.Ordinal);
        if (officialIds.Length == 0) return figures;
        await using var command = new NpgsqlCommand(HolderFiguresSql, connection);
        command.Transaction = transaction;
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

    private static async Task<Dictionary<string, List<PartyLineFigure>>> ReadHolderPartyLinesAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string[] officialIds,
        Dictionary<string, HolderFigures> holderFigures, CancellationToken cancellationToken)
    {
        var partyLines = new Dictionary<string, List<PartyLineFigure>>(StringComparer.Ordinal);
        var withAttendance = officialIds.Where(id => holderFigures.GetValueOrDefault(id)?.AttendanceCongress is not null).ToArray();
        if (withAttendance.Length == 0) return partyLines;
        await using var command = new NpgsqlCommand(HolderPartyLinesSql, connection);
        command.Transaction = transaction;
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

    private sealed record DistrictRows(int? Release, long? PublishedAtUtcTicks, bool HasNames, int Congress, List<DistrictRow> Rows);

    private enum LookupShape { Figures, Members, Strip, Issue }

    private sealed class HolderFigures(string? photoPath, int? attendanceCongress)
    {
        public string? PhotoPath { get; } = photoPath;
        public int? AttendanceCongress { get; } = attendanceCongress;
        public List<MissedVotesFigure> MissedVotes { get; } = [];
    }
}
