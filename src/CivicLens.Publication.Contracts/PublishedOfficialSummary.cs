namespace CivicLens.Publication.Contracts;

/// <summary>
/// The numbers of one official that a ranking needs. The vote rows come from the newest Congress with a vote file of the official.
/// A row with no Congress holds no vote data. CosponsoredBills counts the action records that list the official as a current cosponsor. It is null while any action entry of the release has no cosponsor data, because a partial count would read as a true count.
/// </summary>
public sealed record PublishedOfficialSummary
{
    public const int MaximumRows = 4;

    public required string Id { get; init; }
    public int? Congress { get; init; }
    public required int? CosponsoredBills { get; init; }
    public required PublishedPartyVoteSummary[] PartyVotes { get; init; }
    public required PublishedAttendanceSummary[] Attendance { get; init; }

    public static PublishedOfficialSummary Empty(string id) =>
        new() { Id = id, CosponsoredBills = null, PartyVotes = [], Attendance = [] };

    public void Validate()
    {
        if (!PublishedEvidenceRules.IsBoundedText(Id) || Congress < 1 || CosponsoredBills < 0 ||
            CosponsoredBills > PublicationProtocol.MaximumRecordsPerRelease ||
            PartyVotes is null || PartyVotes.Length > MaximumRows || PartyVotes.Any(row => row is null) ||
            Attendance is null || Attendance.Length > MaximumRows || Attendance.Any(row => row is null) ||
            Congress is null && (PartyVotes.Length > 0 || Attendance.Length > 0))
            throw new InvalidDataException("Published official summary is invalid.");
        foreach (var row in PartyVotes) row.Validate();
        foreach (var row in Attendance) row.Validate();
        if (PartyVotes.Select(row => (row.Chamber, row.Party)).Distinct().Count() != PartyVotes.Length ||
            Attendance.Select(row => row.Chamber).Distinct().Count() != Attendance.Length)
            throw new InvalidDataException("Published official summary rows must be distinct.");
    }
}
