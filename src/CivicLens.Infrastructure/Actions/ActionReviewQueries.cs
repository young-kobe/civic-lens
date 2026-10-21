using CivicLens.Infrastructure.Review;

namespace CivicLens.Infrastructure.Actions;

internal static class ActionReviewQueries
{
    public static readonly string UnpublishedApproved = ReviewQueries.UnpublishedApproved("action", "action_drafts", "record_id");

    public static readonly string CodeApprovedUnpublished = ReviewQueries.CodeApprovedOnly(UnpublishedApproved, "action_review_decisions");
}
