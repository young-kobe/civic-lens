using CivicLens.Infrastructure.Review;

namespace CivicLens.Infrastructure.ExecutiveActions;

internal static class ExecutiveActionReviewQueries
{
    public static readonly string UnpublishedApproved = ReviewQueries.UnpublishedApproved("executiveAction", "executive_action_drafts", "record_id");

    public static readonly string CodeApprovedUnpublished = ReviewQueries.CodeApprovedOnly(UnpublishedApproved, "executive_action_review_decisions");
}
