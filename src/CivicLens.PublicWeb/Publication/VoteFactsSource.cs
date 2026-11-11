using CivicLens.Publication.Contracts;

namespace CivicLens.PublicWeb.Publication;

/// <summary>The cited parts of a vote that its fact list reads. A published vote and a vote page supply them.</summary>
public sealed record VoteFactsSource(PublishedChamber Chamber, PublishedVoteKind Kind, DateOnly VotedOn, PublishedCitation VotedOnCitation,
    PublishedCitation? KindCitation, string Result, PublishedVoteTotals? Totals, PublishedVoteFact[] Facts)
{
    public static VoteFactsSource Of(PublishedVote vote) =>
        new(vote.Chamber, vote.Kind, vote.VotedOn, vote.VotedOnCitation, vote.KindCitation, vote.Result, vote.Totals, vote.Facts);
}
