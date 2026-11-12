using CivicLens.Publication.Contracts;

namespace CivicLens.PublicApi.Votes;

/// <summary>Returns ordered cited facts and the exact published source evidence.</summary>
public sealed record VoteDetail(int Release, VoteRow Vote, string[] IssueIds, PublishedVoteFact[] Facts,
    PublishedVoteSource Source, PublishedDocumentVersion[] Sources, PublishedCitation VotedOnCitation, PublishedCitation? KindCitation)
{
    public PublishedPartyTally[]? PartyTallies { get; init; }
}
