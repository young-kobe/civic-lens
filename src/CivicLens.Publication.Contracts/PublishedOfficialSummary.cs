namespace CivicLens.Publication.Contracts;

/// <summary>
/// The numbers of one official that a ranking needs. The vote rows come from the newest Congress with votes of the official.
/// A row with no Congress holds no vote data. Previous holds the same figures for the next older Congress with votes. A missing Previous means no data, so a reader never reads it as zero.
/// </summary>
public sealed record PublishedOfficialSummary
{
    public required string Id { get; init; }
    public int? Congress { get; init; }
    public required PublishedPartyVoteSummary[] PartyVotes { get; init; }
    public required PublishedAttendanceSummary[] Attendance { get; init; }
    public PublishedVoteFigures? Previous { get; init; }

    public PublishedVoteFigures? FiguresIn(int congress) =>
        congress == Congress ? new() { Congress = congress, PartyVotes = PartyVotes, Attendance = Attendance }
        : Previous?.Congress == congress ? Previous : null;

    public static PublishedOfficialSummary Empty(string id) => new() { Id = id, PartyVotes = [], Attendance = [] };
}
