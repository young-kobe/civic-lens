using CivicLens.Core.Votes;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public static class PublishedVoteSources
{
    public static PublishedVoteSource Of(VoteSource source) => source switch
    {
        VoteSource.CongressGov => PublishedVoteSource.CongressGov,
        VoteSource.Voteview => PublishedVoteSource.Voteview,
        _ => throw new ArgumentOutOfRangeException(nameof(source), source, "Vote source has no published form.")
    };
}
