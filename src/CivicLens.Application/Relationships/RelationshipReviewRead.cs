using CivicLens.Core.Relationships;

namespace CivicLens.Application.Relationships;

public sealed record RelationshipReviewRead(RelationshipReview Review, ApprovedStatement CitedStatement, RelationshipTarget CitedTarget,
    bool StatementChanged, bool TargetChanged)
{
    public bool CitedRecordsChanged => StatementChanged || TargetChanged;

    public RelationshipEditOptions Options => RelationshipEditOptions.For(Review.CurrentRevision, CitedStatement, CitedTarget);
}
