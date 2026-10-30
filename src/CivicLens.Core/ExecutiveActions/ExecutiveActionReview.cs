using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Core.ExecutiveActions;

public sealed record ExecutiveActionReview(string RecordId, ExecutiveActionRevision CurrentRevision, ExecutiveActionRevision? LastApprovedRevision,
    ImmutableArray<ExecutiveActionRevision> Revisions, int ReviewStateVersion, ReviewStatus CurrentStatus,
    ImmutableArray<ReviewDecision> Decisions, ImmutableArray<ReviewDecision> UnresolvedConcerns);
