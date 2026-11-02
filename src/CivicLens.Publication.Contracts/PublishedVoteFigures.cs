namespace CivicLens.Publication.Contracts;

/// <summary>The split votes and the roll calls held and missed by one official in one Congress.</summary>
public sealed record PublishedVoteFigures
{
    public const int MaximumRows = 4;

    public required int Congress { get; init; }
    public required PublishedPartyVoteSummary[] PartyVotes { get; init; }
    public required PublishedAttendanceSummary[] Attendance { get; init; }

    public void Validate()
    {
        if (Congress < 1) throw new InvalidDataException("Published vote figures are invalid.");
        ValidateRows(PartyVotes, Attendance);
    }

    public static void ValidateRows(PublishedPartyVoteSummary[]? partyVotes, PublishedAttendanceSummary[]? attendance)
    {
        if (partyVotes is null || partyVotes.Length > MaximumRows || partyVotes.Any(row => row is null) ||
            attendance is null || attendance.Length > MaximumRows || attendance.Any(row => row is null))
            throw new InvalidDataException("Published vote figures are invalid.");
        foreach (var row in partyVotes) row.Validate();
        foreach (var row in attendance) row.Validate();
        if (partyVotes.Select(row => (row.Chamber, row.Party)).Distinct().Count() != partyVotes.Length ||
            attendance.Select(row => row.Chamber).Distinct().Count() != attendance.Length)
            throw new InvalidDataException("Published vote figure rows must be distinct.");
    }
}
