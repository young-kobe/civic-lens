namespace CivicLens.Publication.Contracts;

/// <summary>One roll call vote of one official. The record ID names the vote record that holds the evidence.</summary>
public sealed record PublishedOfficialVote
{
    public const int MaximumHeadlineLength = 256;
    public const int MaximumBillTypeLength = 16;
    public const int MaximumCastLength = 32;

    public required string RecordId { get; init; }
    public required DateOnly VotedOn { get; init; }
    public required PublishedChamber Chamber { get; init; }
    public required int Congress { get; init; }
    public required int Session { get; init; }
    public required int RollCallNumber { get; init; }
    public string? BillType { get; init; }
    public int? BillNumber { get; init; }
    public required string Cast { get; init; }
    public required string Headline { get; init; }

    public static PublishedOfficialVote Of(PublishedVote vote, PublishedMemberVote member) => new()
    {
        RecordId = vote.RecordId,
        VotedOn = vote.VotedOn,
        Chamber = vote.Chamber,
        Congress = vote.Congress,
        Session = vote.Session,
        RollCallNumber = vote.RollCallNumber,
        BillType = vote.BillType,
        BillNumber = vote.BillNumber,
        Cast = member.Cast,
        Headline = vote.Headline
    };

    public void Validate()
    {
        if (!PublicationProtocol.IsRecordId(PublishedRecordKind.Vote, RecordId) || !Enum.IsDefined(Chamber) || Congress < 1 || Session is not (1 or 2) ||
            RollCallNumber < 1 || !IsText(Cast, MaximumCastLength) || !IsText(Headline, MaximumHeadlineLength) ||
            (BillType is null) != (BillNumber is null) || BillType is not null && (!IsText(BillType, MaximumBillTypeLength) || BillNumber < 1))
            throw new InvalidDataException("Published official vote is invalid.");
    }

    private static bool IsText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength;
}
