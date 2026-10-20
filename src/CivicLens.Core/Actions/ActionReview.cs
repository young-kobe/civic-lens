using System.Collections.Immutable;
using CivicLens.Core.Review;

namespace CivicLens.Core.Actions;

public sealed record ActionReview(string RecordId, ActionDraftRevision CurrentRevision, ActionDraftRevision? LastApprovedRevision,
    ImmutableArray<ActionDraftRevision> Revisions, int ReviewStateVersion, ReviewStatus CurrentStatus, ImmutableArray<ReviewDecision> Decisions,
    ImmutableArray<ReviewDecision> UnresolvedConcerns);
