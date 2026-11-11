using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>Everything that the vote page shows, read from one active release. Party counts are null when the release does not hold them.</summary>
public sealed record VotePageData(
    int Release,
    string RecordId,
    int RevisionNumber,
    PublishedChamber Chamber,
    int Congress,
    int Session,
    int RollCall,
    DateOnly VotedOn,
    PublishedVoteKind Kind,
    PublishedVoteSource Source,
    string Headline,
    string Summary,
    PublishedTextOrigin TextOrigin,
    string? Label,
    string Result,
    PublishedVoteAlignment? Alignment,
    PublishedVoteTotals? Totals,
    PublishedPartyTally[]? PartyTallies,
    PublishedCitation VotedOnCitation,
    PublishedCitation? KindCitation,
    PublishedVoteFact[] Facts,
    PublishedDocumentVersion[] Sources,
    VoteMemberData[] Members,
    VoteBillData? Bill,
    VoteIssueData[] Issues,
    string? BillType,
    int? BillNumber)
{
    public VoteFactsSource FactsSource => new(Chamber, Kind, VotedOn, VotedOnCitation, KindCitation, Result, Totals, Facts);

    public VoteWordingSource WordingSource => new(Kind, Chamber, Source, Result, KindCitation, Facts, BillType, BillNumber);

    public string ShortName => $"{Chamber} roll call {RollCall}";
}
