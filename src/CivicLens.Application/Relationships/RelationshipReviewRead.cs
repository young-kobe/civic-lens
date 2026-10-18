using CivicLens.Core.Relationships;

namespace CivicLens.Application.Relationships;

public sealed record RelationshipReviewRead(RelationshipReview Review, ApprovedStatement CitedStatement, RelationshipTarget CitedTarget,
    bool StatementChanged, bool TargetChanged, RelationshipEditOptions Options)
{
    public bool CitedRecordsChanged => StatementChanged || TargetChanged;
}
