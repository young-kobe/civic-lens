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
    public required string Result { get; init; }
    public PublishedVoteTotals? Totals { get; init; }
    public required bool SplitVote { get; init; }
    public required string Party { get; init; }
    public string? PartyMajority { get; init; }
    public required string Headline { get; init; }
    public required PublishedVoteKind Kind { get; init; }
    public required string[] IssueIds { get; init; }

    [JsonIgnore]
    public bool IsCast => Cast != NotVoting;

    public static string? SideOf(string cast) => cast switch
    {
        "Yea" or "Aye" => PublishedPartyMajority.Yea,
        "Nay" or "No" => PublishedPartyMajority.Nay,
        _ => null
    };

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
        Result = vote.Result,
        Totals = vote.Totals,
        SplitVote = vote.IsSplit(),
        Party = member.Party,
        PartyMajority = vote.MajorityOf(member.Party),
        Headline = vote.Headline,
        Kind = vote.Kind,
        IssueIds = vote.TrackedIssueIds
    };

    public void Validate()
    {
        if (!PublicationProtocol.IsRecordId(PublishedRecordKind.Vote, RecordId) || !Enum.IsDefined(Chamber) || Congress < 1 || Session is not (1 or 2) ||
            RollCallNumber < 1 || !Enum.IsDefined(Kind) || !PublishedRecordEntry.AreFilterValues(IssueIds) || !IsText(Cast, PublishedVote.MaximumCastLength) || !IsText(Headline, PublishedVote.MaximumHeadlineLength) ||
            !IsText(Result, PublishedVote.MaximumHeadlineLength) || !IsText(Party, PublishedVote.MaximumHeadlineLength) || PartyMajority is not (null or PublishedPartyMajority.Yea or PublishedPartyMajority.Nay) ||
            SplitVote && (!Kind.IsSubstantive() || Totals is null || PartyMajority is null && PublishedPartyMajority.IsMajorParty(Party)) || (BillType is null) != (BillNumber is null) || BillType is not null && (!IsText(BillType, PublishedVote.MaximumBillTypeLength) || BillNumber < 1))
            throw new InvalidDataException("Published official vote is invalid.");
        Totals?.Validate();
    }

    private static bool IsText(string? value, int maximumLength) =>
        !string.IsNullOrWhiteSpace(value) && value.Length <= maximumLength;
}
