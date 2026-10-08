using System.Collections.Immutable;

namespace CivicLens.Core.Review;

public sealed record DocumentChangeReview(string DraftId, DocumentChangeDraftRevision CurrentRevision,
    int ReviewStateVersion, ImmutableArray<DocumentChangeDraftRevision> Revisions,
    ImmutableArray<ReviewDecision> Decisions, ImmutableArray<ReviewDecision> UnresolvedConcerns);
