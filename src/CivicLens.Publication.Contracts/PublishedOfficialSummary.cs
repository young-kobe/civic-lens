namespace CivicLens.Publication.Contracts;

/// <summary>
/// The numbers of one official that a ranking needs. The vote rows come from the newest Congress with a vote file of the official.
/// A row with no Congress holds no vote data. Previous holds the same figures for the next older Congress with a vote file. A missing Previous means no data, so a reader never reads it as zero. CosponsoredBills counts the action records that list the official as a current cosponsor. It is null while any action entry of the release has no cosponsor data, because a partial count would read as a true count.
/// </summary>
public sealed record PublishedOfficialSummary
{
    public required string Id { get; init; }
    public int? Congress { get; init; }
    public required int? CosponsoredBills { get; init; }
    public required PublishedPartyVoteSummary[] PartyVotes { get; init; }
    public required PublishedAttendanceSummary[] Attendance { get; init; }
    public PublishedVoteFigures? Previous { get; init; }

    public PublishedVoteFigures? FiguresIn(int congress) =>
        congress == Congress ? new() { Congress = congress, PartyVotes = PartyVotes, Attendance = Attendance }
        : Previous?.Congress == congress ? Previous : null;

    public static PublishedOfficialSummary Empty(string id) =>
        new() { Id = id, CosponsoredBills = null, PartyVotes = [], Attendance = [] };

    public void Validate()
    {
        if (!PublishedEvidenceRules.IsBoundedText(Id) || Congress < 1 || CosponsoredBills < 0 ||
            CosponsoredBills > PublicationProtocol.MaximumRecordsPerRelease ||
            Congress is null && (PartyVotes?.Length > 0 || Attendance?.Length > 0 || Previous is not null))
            throw new InvalidDataException("Published official summary is invalid.");
        PublishedVoteFigures.ValidateRows(PartyVotes, Attendance);
        if (Previous is null) return;
        Previous.Validate();
        if (Previous.Congress >= Congress) throw new InvalidDataException("Published official summary is invalid.");
    }
}
