using CivicLens.Infrastructure.Review;

namespace CivicLens.Infrastructure.Briefs;

internal static class BriefReviewQueries
{
    public static readonly string UnpublishedApproved = ReviewQueries.UnpublishedApproved("brief", "brief_drafts", "record_id");
}
