using CivicLens.Core.Relationships;

namespace CivicLens.Application.Relationships;

public sealed record RelationshipReviewItem(RelationshipReview Review, string StatementHeadline, string TargetHeadline, bool CitedRecordsChanged);
