using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Core.Relationships;

public sealed record RelationshipReview(string RecordId, RelationshipRevision CurrentRevision, RelationshipRevision? LastApprovedRevision,
    ImmutableArray<RelationshipRevision> Revisions, int ReviewStateVersion, ReviewStatus CurrentStatus,
    ImmutableArray<ReviewDecision> Decisions, ImmutableArray<ReviewDecision> UnresolvedConcerns);
