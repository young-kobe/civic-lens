namespace CivicLens.Publication.Contracts;

/// <summary>The split votes and the roll calls held and missed by one official in one Congress.</summary>
public sealed record PublishedVoteFigures
{
    public required int Congress { get; init; }
    public required PublishedPartyVoteSummary[] PartyVotes { get; init; }
    public required PublishedAttendanceSummary[] Attendance { get; init; }
}
