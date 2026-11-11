namespace CivicLens.Publication.Contracts;

/// <summary>The cited parts of a vote that the approved wording reads. A published vote and a vote detail answer both supply them.</summary>
public sealed record VoteWordingSource(PublishedVoteKind Kind, PublishedChamber Chamber, PublishedVoteSource Source, string? Result,
    PublishedCitation? KindCitation, PublishedVoteFact[]? Facts, string? BillType, int? BillNumber)
{
    public static VoteWordingSource Of(PublishedVote vote) =>
        new(vote.Kind, vote.Chamber, vote.Source, vote.Result, vote.KindCitation, vote.Facts, vote.BillType, vote.BillNumber);
}
