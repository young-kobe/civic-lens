using CivicLens.Infrastructure.Review;

namespace CivicLens.Infrastructure.Relationships;

internal static class RelationshipReviewQueries
{
    public static readonly string UnpublishedApproved = ReviewQueries.UnpublishedApproved("relationship", "relationship_drafts", "record_id");
}
