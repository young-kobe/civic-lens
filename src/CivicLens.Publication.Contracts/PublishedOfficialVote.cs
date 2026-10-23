using System.Text.Json.Serialization;

namespace CivicLens.Publication.Contracts;

/// <summary>One roll call vote of one official. The record ID names the vote record that holds the evidence.</summary>
public sealed record PublishedOfficialVote
{
    public const string NotVoting = "Not Voting";

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
    public required PublishedVoteKind Kind { get; init; }
    public required string[] IssueIds { get; init; }

    [JsonIgnore]
    public bool IsCast => Cast != NotVoting;

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
        Headline = vote.Headline,
        Kind = vote.Kind,
        IssueIds = vote.TrackedIssueIds
    };

    public void Validate()
    {
        if (!PublicationProtocol.IsRecordId(PublishedRecordKind.Vote, RecordId) || !Enum.IsDefined(Chamber) || Congress < 1 || Session is not (1 or 2) ||
            RollCallNumber < 1 || !Enum.IsDefined(Kind) || !PublishedRecordEntry.AreFilterValues(IssueIds) || !IsText(Cast, PublishedVote.MaximumCastLength) || !IsText(Headline, PublishedVote.MaximumHeadlineLength) ||
            (BillType is null) != (BillNumber is null) || BillType is not null && (!IsText(BillType, PublishedVote.MaximumBillTypeLength) || BillNumber < 1))
            throw new InvalidDataException("Published official vote is invalid.");
    }

    private static bool IsText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength;
}
