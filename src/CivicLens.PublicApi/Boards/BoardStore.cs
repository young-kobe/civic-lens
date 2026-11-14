using System.Data;
using CivicLens.PublicApi.Officials;
using CivicLens.PublicApi.PartyAgreement;
using CivicLens.Publication.Contracts;
using Npgsql;

namespace CivicLens.PublicApi.Boards;

/// <summary>Builds all four party boards from one active release snapshot.</summary>
public sealed class BoardStore(NpgsqlDataSource views, PartyAgreementStore agreements)
{
    private const string OfficialsSql = """
        SELECT official_id, name
          FROM public_api.officials
         ORDER BY official_id
        """;

    private const string TermsSql = """
        SELECT official_id, office, kind, state, district, party, caucus, start_on, end_on
          FROM public_api.release_official_terms
         WHERE official_id = ANY(@official_ids)
         ORDER BY official_id, "position"
        """;

    // One pass over the vote figures of every member of the active release. The rows of people who are not officials still count for incompleteness.
    private const string VoteFiguresSql = """
        SELECT official_id, chamber, congress, party, held, missed, first_voted_on, last_voted_on, cast_count, against_party, unwritten_votes
          FROM public_api.official_vote_parties
         ORDER BY official_id, congress DESC, chamber, party
        """;

    public async Task<BoardAnswer?> FindAsync(CancellationToken cancellationToken)
    {
        await using var connection = await views.OpenConnectionAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(IsolationLevel.RepeatableRead, cancellationToken);
        var release = await ReadReleaseAsync(connection, transaction, cancellationToken);
        if (release is null)
        {
            await transaction.CommitAsync(cancellationToken);
            return null;
        }

        var officials = await ReadOfficialsAsync(connection, transaction, cancellationToken);
        var officialIds = officials.Select(official => official.OfficialId).ToArray();
        var terms = await ReadTermsAsync(connection, transaction, officialIds, cancellationToken);
        var figures = await ReadVoteFiguresAsync(connection, transaction, [.. officialIds], cancellationToken);
        var congressesByOfficial = figures.Attendance.Keys.GroupBy(key => key.OfficialId, StringComparer.Ordinal)
            .ToDictionary(group => group.Key, group => group.Select(key => key.Congress).Distinct().OrderDescending().Take(2).ToArray(), StringComparer.Ordinal);
        var pages = OfficialBoardPage.All(
            [.. officials.Select(official => official.Listing)],
            [.. officials.Select(official => SummaryOf(official.OfficialId, congressesByOfficial, figures.Attendance, figures.PartyVotes))]);

        var relevantCongresses = pages.SelectMany(page => new[] { page.Congress, page.NewerCongress }
                .Where(congress => congress is not null)
                .Select(congress => (page.Board.Chamber, Congress: congress!.Value)))
            .Distinct();
        var incompleteVoteInputs = relevantCongresses.Any(figures.IncompleteCongresses.Contains);
        var agreementAnswers = new Dictionary<PublishedChamber, PartyAgreementAnswer?>();
        foreach (var chamber in Enum.GetValues<PublishedChamber>())
        {
            var boardCongress = pages.First(page => page.Board.Chamber == chamber).Congress;
            var requestCongress = boardCongress ?? 1;
            agreementAnswers[chamber] = await agreements.FindAsync(connection, transaction,
                new PartyAgreementRequest(chamber, requestCongress), cancellationToken);
        }

        if (incompleteVoteInputs || agreementAnswers.Values.Any(answer => answer?.Agreement is null))
        {
            var unknownBoards = OfficialBoard.All.Select(UnknownBoard).ToArray();
            var unknownAgreements = Enum.GetValues<PublishedChamber>()
                .Select(chamber => new BoardAgreementAnswer(chamber, null)).ToArray();
            await transaction.CommitAsync(cancellationToken);
            return new(release.Value, unknownBoards, unknownAgreements);
        }

        var officialsById = officials.ToDictionary(official => official.OfficialId, StringComparer.Ordinal);
        var boards = pages.Select(page => BoardOf(page, officialsById, terms)).ToArray();
        var boardAgreements = Enum.GetValues<PublishedChamber>().Select(chamber =>
        {
            var congress = pages.First(page => page.Board.Chamber == chamber).Congress;
            var agreement = congress is null ? null : agreementAnswers[chamber]!.Agreement;
            return new BoardAgreementAnswer(chamber, agreement);
        }).ToArray();
        await transaction.CommitAsync(cancellationToken);
        return new(release.Value, boards, boardAgreements);
    }

    private static async Task<int?> ReadReleaseAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        await using var command = new NpgsqlCommand("SELECT release_number FROM public_api.active_release LIMIT 1", connection, transaction);
        var value = await command.ExecuteScalarAsync(cancellationToken);
        return value is int release ? release : null;
    }

    private static async Task<BoardOfficial[]> ReadOfficialsAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        CancellationToken cancellationToken)
    {
        var officials = new List<BoardOfficial>();
        await using var command = new NpgsqlCommand(OfficialsSql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            var name = reader.GetString(1);
            var profile = new PublishedOfficialProfile { Id = id, Name = name, Terms = [] };
            officials.Add(new(id, new(profile, 0)));
        }
        return [.. officials];
    }

    private static async Task<Dictionary<string, List<OfficialTerm>>> ReadTermsAsync(NpgsqlConnection connection,
        NpgsqlTransaction transaction, string[] officialIds, CancellationToken cancellationToken)
    {
        var terms = new Dictionary<string, List<OfficialTerm>>(StringComparer.Ordinal);
        if (officialIds.Length == 0) return terms;
        await using var command = new NpgsqlCommand(TermsSql, connection, transaction);
        command.Parameters.AddWithValue("official_ids", officialIds);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var id = reader.GetString(0);
            if (!terms.TryGetValue(id, out var rows)) terms.Add(id, rows = []);
            rows.Add(new(reader.GetString(1), Enum.Parse<PublishedOfficeKind>(reader.GetString(2)),
                reader.IsDBNull(3) ? null : reader.GetString(3),
                reader.IsDBNull(4) ? null : reader.GetInt32(4),
                reader.IsDBNull(5) ? null : reader.GetString(5),
                reader.IsDBNull(6) ? null : reader.GetString(6),
                reader.GetFieldValue<DateOnly>(7),
                reader.IsDBNull(8) ? null : reader.GetFieldValue<DateOnly>(8)));
        }
        return terms;
    }

    private static async Task<BoardVoteFigures> ReadVoteFiguresAsync(NpgsqlConnection connection, NpgsqlTransaction transaction,
        HashSet<string> officialIds, CancellationToken cancellationToken)
    {
        var figures = new BoardVoteFigures([], [], []);
        await using var command = new NpgsqlCommand(VoteFiguresSql, connection, transaction);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var officialId = reader.GetString(0);
            var chamber = Enum.Parse<PublishedChamber>(reader.GetString(1));
            var congress = reader.GetInt32(2);
            var party = reader.IsDBNull(3) ? null : reader.GetString(3);
            if (party is null || reader.GetInt64(10) > 0) figures.IncompleteCongresses.Add((chamber, congress));
            if (!officialIds.Contains(officialId)) continue;
            AddAttendance(figures.Attendance, (officialId, congress), chamber, reader);
            if (party is "Democrat" or "Republican") AddPartyVotes(figures.PartyVotes, (officialId, congress), chamber, party, reader);
        }
        return figures;
    }

    // Each party row of an official repeats the totals of its chamber and Congress, so the first row gives the attendance.
    private static void AddAttendance(Dictionary<(string OfficialId, int Congress), List<PublishedAttendanceSummary>> attendance,
        (string OfficialId, int Congress) key, PublishedChamber chamber, NpgsqlDataReader reader)
    {
        if (!attendance.TryGetValue(key, out var figures)) attendance.Add(key, figures = []);
        if (figures.Any(figure => figure.Chamber == chamber)) return;
        figures.Add(new PublishedAttendanceSummary
        {
            Chamber = chamber,
            Held = checked((int)reader.GetInt64(4)),
            Missed = checked((int)reader.GetInt64(5)),
            First = reader.GetFieldValue<DateOnly>(6),
            Last = reader.GetFieldValue<DateOnly>(7)
        });
    }

    private static void AddPartyVotes(Dictionary<(string OfficialId, int Congress), List<PublishedPartyVoteSummary>> partyVotes,
        (string OfficialId, int Congress) key, PublishedChamber chamber, string party, NpgsqlDataReader reader)
    {
        if (!partyVotes.TryGetValue(key, out var figures)) partyVotes.Add(key, figures = []);
        figures.Add(new PublishedPartyVoteSummary
        {
            Chamber = chamber,
            Party = party,
            SplitVotesCast = checked((int)reader.GetInt64(8)),
            Breaks = checked((int)reader.GetInt64(9))
        });
    }

    private static PublishedOfficialSummary SummaryOf(string officialId,
        Dictionary<string, int[]> congressesByOfficial,
        Dictionary<(string OfficialId, int Congress), List<PublishedAttendanceSummary>> attendance,
        Dictionary<(string OfficialId, int Congress), List<PublishedPartyVoteSummary>> partyVotes)
    {
        var congresses = congressesByOfficial.GetValueOrDefault(officialId) ?? [];
        if (congresses.Length == 0) return PublishedOfficialSummary.Empty(officialId);
        List<PublishedPartyVoteSummary> PartyRows(int congress) => partyVotes.GetValueOrDefault((officialId, congress)) ?? [];
        List<PublishedAttendanceSummary> AttendanceRows(int congress) => attendance.GetValueOrDefault((officialId, congress)) ?? [];
        var current = congresses[0];
        var previous = congresses.Length < 2 ? null : new PublishedVoteFigures
        {
            Congress = congresses[1],
            PartyVotes = [.. PartyRows(congresses[1])],
            Attendance = [.. AttendanceRows(congresses[1])]
        };
        return new()
        {
            Id = officialId,
            Congress = current,
            CosponsoredBills = null,
            PartyVotes = [.. PartyRows(current)],
            Attendance = [.. AttendanceRows(current)],
            Previous = previous
        };
    }

    private static BoardPageAnswer BoardOf(OfficialBoardPage page, Dictionary<string, BoardOfficial> officials,
        Dictionary<string, List<OfficialTerm>> terms)
    {
        var members = page.Members.Select(member =>
        {
            var official = officials[member.Official.Profile.Id];
            var congressTerms = page.Congress is { } congress
                ? TermsFor(terms.GetValueOrDefault(official.OfficialId) ?? [], page.Board.Chamber, congress)
                : [];
            var missed = member.Missed is { } value
                ? new BoardMissedVotesAnswer(value.Missed, value.Held, value.First, value.Last,
                    OfficialScopes.MissedVotes(page.Board.Chamber, page.Congress!.Value))
                : null;
            return new BoardMemberAnswer(official.OfficialId, member.Official.Profile.Name, congressTerms,
                member.Cast, member.Breaks, missed, member.PagePath,
                OfficialScopes.PartyLine(page.Board.Chamber, page.Congress!.Value, page.Board.Party));
        }).ToArray();
        return new(new(page.Board.Chamber, page.Board.Party), page.Congress, page.NewerCongress, page.WithoutVotes, page.NotInCongress,
            page.WithoutMajorParty, members, [.. page.Breakers.Select(member => member.Official.Profile.Id)],
            [.. page.Loyalists.Select(member => member.Official.Profile.Id)],
            [.. page.MostMissed.Select(member => member.Official.Profile.Id)],
            [.. page.TooFewSplitVotes.Select(member => member.Official.Profile.Id)]);
    }

    private static BoardPageAnswer UnknownBoard(OfficialBoard board) => new(new(board.Chamber, board.Party), null, null,
        null, null, null, null, null, null, null, null);

    private static BoardTermAnswer[] TermsFor(List<OfficialTerm> terms, PublishedChamber chamber, int congress)
    {
        var (start, end) = CongressRange(congress);
        var expectedOffice = chamber == PublishedChamber.House ? "House" : "Senate";
        return [.. terms.Where(term => term.Office == expectedOffice && term.StartsOn < end && (term.EndsOn is null || term.EndsOn >= start))
            .Select(term => new BoardTermAnswer(term.Office, term.Kind, term.State, term.District, term.Party, term.Caucus,
                term.StartsOn, term.EndsOn, term.Office == "House"
                    ? NonStateSeats.All.SingleOrDefault(seat => seat.Code == term.State)?.HouseOffice
                    : null))];
    }

    private static (DateOnly Start, DateOnly End) CongressRange(int congress)
    {
        if (congress < 1) throw new InvalidDataException("The board Congress is outside the supported calendar range.");
        try
        {
            return (CongressCalendar.StartOf(congress), CongressCalendar.StartOf(checked(congress + 1)));
        }
        catch (Exception exception) when (exception is ArgumentOutOfRangeException or OverflowException)
        {
            throw new InvalidDataException("The board Congress is outside the supported calendar range.", exception);
        }
    }

    private sealed record BoardOfficial(string OfficialId, OfficialListing Listing);

    private sealed record BoardVoteFigures(
        Dictionary<(string OfficialId, int Congress), List<PublishedAttendanceSummary>> Attendance,
        Dictionary<(string OfficialId, int Congress), List<PublishedPartyVoteSummary>> PartyVotes,
        HashSet<(PublishedChamber Chamber, int Congress)> IncompleteCongresses);
}
