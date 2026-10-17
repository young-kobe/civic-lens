using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Core.Votes;

public sealed record VoteReview(string RecordId, VoteDraftRevision CurrentRevision, VoteDraftRevision? LastApprovedRevision,
    ImmutableArray<VoteDraftRevision> Revisions, int ReviewStateVersion, ReviewStatus CurrentStatus, ImmutableArray<ReviewDecision> Decisions,
    ImmutableArray<ReviewDecision> UnresolvedConcerns);
