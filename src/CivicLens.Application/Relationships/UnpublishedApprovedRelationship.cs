using CivicLens.Core.Relationships;

namespace CivicLens.Application.Relationships;

public sealed record UnpublishedApprovedRelationship(RelationshipReview Relationship, bool CitedRecordsChanged);
