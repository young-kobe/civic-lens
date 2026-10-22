using CivicLens.Core.Votes;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

public static class PublishedVoteKinds
{
    public static PublishedVoteKind Of(VoteKind kind) => kind switch
    {
        VoteKind.Rule => PublishedVoteKind.Rule,
        VoteKind.PreviousQuestion => PublishedVoteKind.PreviousQuestion,
        VoteKind.MotionToRecommit => PublishedVoteKind.MotionToRecommit,
        VoteKind.Amendment => PublishedVoteKind.Amendment,
        VoteKind.Suspension => PublishedVoteKind.Suspension,
        VoteKind.Passage => PublishedVoteKind.Passage,
        VoteKind.ConferenceReport => PublishedVoteKind.ConferenceReport,
        VoteKind.VetoOverride => PublishedVoteKind.VetoOverride,
        VoteKind.Nomination => PublishedVoteKind.Nomination,
        VoteKind.Cloture => PublishedVoteKind.Cloture,
        VoteKind.MotionToProceed => PublishedVoteKind.MotionToProceed,
        VoteKind.Other => PublishedVoteKind.Other,
        _ => throw new ArgumentOutOfRangeException(nameof(kind), kind, "Vote kind has no published form.")
    };
}
