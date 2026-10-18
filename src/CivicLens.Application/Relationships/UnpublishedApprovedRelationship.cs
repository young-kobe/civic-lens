using CivicLens.Core.Relationships;

namespace CivicLens.Application.Relationships;

public sealed record UnpublishedApprovedRelationship(RelationshipReview Relationship, string StatementHeadline, string TargetHeadline,
    int? PublishedRevisionNumber, bool CitedRecordsChanged);
