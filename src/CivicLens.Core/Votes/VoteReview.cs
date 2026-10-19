using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Core.Votes;

public sealed record VoteReview(string RecordId, VoteDraftRevision CurrentRevision, VoteDraftRevision? LastApprovedRevision,
    ImmutableArray<VoteDraftRevision> Revisions, int ReviewStateVersion, ReviewStatus CurrentStatus, ImmutableArray<ReviewDecision> Decisions,
    ImmutableArray<ReviewDecision> UnresolvedConcerns)
{
    public bool IsBulkSafe => UnresolvedConcerns.IsEmpty && (IsProcedural || ChangesOnlyMemberVotesSinceApproval);

    private bool IsProcedural => CurrentRevision.Kind is VoteKind.Rule or VoteKind.PreviousQuestion or VoteKind.MotionToRecommit;

    private bool ChangesOnlyMemberVotesSinceApproval => LastApprovedRevision is { } approved &&
        ReviewTextRules.HasSameText(CurrentRevision, approved) && CurrentRevision.Kind == approved.Kind;
}
