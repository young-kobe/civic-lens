using CivicLens.PublicApi.Http;
using CivicLens.PublicApi.Officials;
using CivicLens.PublicApi.Records;
using CivicLens.Publication.Contracts;
using Npgsql;
using NpgsqlTypes;

namespace CivicLens.PublicApi.Maps;

/// <summary>Reads the dated holders and the selected layer of the active release in one read-only snapshot.</summary>
public sealed partial class MapStore(RecordSnapshot snapshot)
{
    private const string ReleaseSql = """
        SELECT r.release_number, r.published_at_utc_ticks, i.name
          FROM public_api.active_release r
          LEFT JOIN public_api.release_issues i ON i.release_number = r.release_number AND i.issue_id = @issue
        """;

    private const string TermsSql = """
        WITH dated AS (
            SELECT t.*, dense_rank() OVER (PARTITION BY t.official_id ORDER BY t.start_on DESC) AS newest
              FROM public_api.release_official_terms t
             WHERE t.release_number = @release AND t.kind = @kind AND t.start_on <= @asOf
               AND (t.end_on IS NULL OR t.end_on >= @asOf)
        )
        SELECT t.official_id, o.name, t.office, t.kind, t.state, t.district, t.party, t.caucus, t.start_on, t.end_on, t.senate_class
          FROM dated t
          JOIN public_api.release_officials o ON o.release_number = t.release_number AND o.official_id = t.official_id
         WHERE t.newest = 1
         ORDER BY t.official_id COLLATE "C", t."position"
        """;

    public async Task<MapAnswer?> FindAsync(MapRequest request, CancellationToken cancellationToken)
    {
        var (connection, transaction) = await snapshot.OpenAsync(cancellationToken);
        await using var ownedConnection = connection;
        await using var ownedTransaction = transaction;
        var release = await ReadReleaseAsync(connection, transaction, request.Issue, cancellationToken);
        if (release is null) return null;

        var releaseCongress = CongressCalendar.CongressOn(release.Day);
        if (request.Congress is { } selected && (releaseCongress is null || selected > releaseCongress))
            throw new ArgumentException("congress is later than the Congress of the active release.", "congress");
        if ((request.Congress ?? releaseCongress) is not { } congress)
            return new(release.Number, null, request.Chamber, request.Layer, null, release.Issue, [], []);

        // With no future Congress allowed, a historical selection clamps to its last day.
        var asOf = congress == releaseCongress ? release.Day : CongressCalendar.StartOf(congress + 1).AddDays(-1);
        var members = await ReadMembersAsync(connection, transaction, release.Number, request.Chamber, asOf, cancellationToken);
        var seats = SeatsOf(members, request.Chamber);
        if (members.Length > 0)
        {
            if (request.Layer == MapLayer.Attendance)
                members = await ReadAttendanceAsync(connection, transaction, release.Number, congress, request.Chamber, members, cancellationToken);
            else if (release.Issue is { } issue)
                members = await ReadActivityAsync(connection, transaction, release.Number, congress, issue.Id, members, cancellationToken);
        }

        return new(release.Number, congress, request.Chamber, request.Layer, asOf, release.Issue, members, seats);
    }

    private static async Task<MapRelease?> ReadReleaseAsync(NpgsqlConnection connection, NpgsqlTransaction transaction, string? issueId,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(ReleaseSql, connection, transaction);
        command.Parameters.AddWithValue("issue", NpgsqlDbType.Text, (object?)issueId ?? DBNull.Value);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken)) return null;
        var issueName = ViewColumns.Text(reader, 2);
        if (issueId is not null && issueName is null)
            throw new ArgumentException("issue must name an issue in the active release.", "issue");

        var day = DateOnly.FromDateTime(ViewColumns.Utc(reader, 1)!.Value.UtcDateTime);
        return new(reader.GetInt32(0), day, issueId is null ? null : new MapIssueAnswer(issueId, issueName!));
    }

    private static async Task<MapMemberAnswer[]> ReadMembersAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        int release, PublishedChamber chamber, DateOnly asOf, CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand(TermsSql, connection, transaction);
        command.Parameters.AddWithValue("release", release);
        command.Parameters.AddWithValue("kind", chamber == PublishedChamber.House ? nameof(PublishedOfficeKind.Representative) : nameof(PublishedOfficeKind.Senator));
        command.Parameters.AddWithValue("asOf", asOf);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var terms = new Dictionary<string, (string Name, List<PublishedOfficeTerm> Terms)>(StringComparer.Ordinal);

        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            if (!terms.TryGetValue(id, out var member))
            {
                if (terms.Count == PublishedOfficials.MaximumOfficials)
                    throw new InvalidDataException("The release exceeds its official count limit.");
                member = (reader.GetString(1), []);
                terms.Add(id, member);
            }
            if (member.Terms.Count == PublishedOfficials.MaximumTerms)
                throw new InvalidDataException("An official exceeds the release term count limit.");
            member.Terms.Add(new()
            {
                Office = reader.GetString(2),
                Kind = ViewColumns.Enum<PublishedOfficeKind>(reader, 3),
                State = ViewColumns.Text(reader, 4),
                District = ViewColumns.Number(reader, 5),
                Party = ViewColumns.Text(reader, 6),
                Caucus = ViewColumns.Text(reader, 7),
                Start = reader.GetFieldValue<DateOnly>(8),
                End = ViewColumns.Date(reader, 9),
                SenateClass = ViewColumns.Number(reader, 10)
            });
        }

        return [.. terms.Select(pair =>
        {
            var (name, latest) = pair.Value;
            var first = latest[0];
            return new MapMemberAnswer(pair.Key, name, [.. latest], latest.All(term => term.Party == first.Party) ? first.Party : null,
                latest.All(term => term.End == first.End) ? first.End : null, null, null,
                PublicationProtocol.OfficialPagePath(Uri.EscapeDataString(pair.Key)));
        })];
    }

    private static MapSeatAnswer[] SeatsOf(MapMemberAnswer[] members, PublishedChamber chamber) =>
        [.. members.Select(member => (member.OfficialId, Seat: SeatOf(member.Terms, chamber)))
            .Where(claim => claim.Seat is not null)
            .GroupBy(claim => claim.Seat!)
            .OrderBy(group => group.Key.State, StringComparer.Ordinal)
            .ThenBy(group => group.Key.District)
            .ThenBy(group => group.Key.SenateClass)
            .Select(group => group.Key with { OfficialId = group.Count() == 1 ? group.First().OfficialId : null })];

    private static MapSeatAnswer? SeatOf(PublishedOfficeTerm[] terms, PublishedChamber chamber)
    {
        var first = terms[0];
        if (first.State is not { } state || terms.Any(term => term.State != state))
            return null;

        return chamber switch
        {
            PublishedChamber.House when first.District is { } district && terms.All(term => term.District == district) => new(state, district, null, null),
            PublishedChamber.Senate when first.SenateClass is { } senateClass && terms.All(term => term.SenateClass == senateClass) => new(state, null, senateClass, null),
            _ => null
        };
    }

    private sealed record MapRelease(int Number, DateOnly Day, MapIssueAnswer? Issue);
}
