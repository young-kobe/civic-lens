using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Core.Briefs;

public sealed record BriefReview(string RecordId, BriefRevision CurrentRevision,
    ImmutableArray<BriefRevision> Revisions, int ReviewStateVersion, ReviewStatus CurrentStatus, ImmutableArray<ReviewDecision> Decisions,
    ImmutableArray<ReviewDecision> UnresolvedConcerns);
