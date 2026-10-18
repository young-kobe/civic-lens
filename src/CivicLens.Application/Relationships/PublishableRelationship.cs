using CivicLens.Core.Relationships;

namespace CivicLens.Application.Relationships;

public sealed record PublishableRelationship(RelationshipReview Review, ApprovedStatement Statement, RelationshipTarget Target,
    bool CitedRecordsChanged);
