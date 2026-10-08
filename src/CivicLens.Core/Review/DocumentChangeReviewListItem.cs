namespace CivicLens.Core.Review;

public sealed record DocumentChangeReviewListItem(string DraftId, DocumentChangeDraftRevision CurrentRevision,
    int ReviewStateVersion, DocumentChangeReviewStatus CurrentStatus, int UnresolvedConcernCount);
