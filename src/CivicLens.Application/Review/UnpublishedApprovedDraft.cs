using CivicLens.Core.Review;

namespace CivicLens.Application.Review;

public sealed record UnpublishedApprovedDraft(DocumentChangeReviewListItem Draft, int? PublishedRevisionNumber);
