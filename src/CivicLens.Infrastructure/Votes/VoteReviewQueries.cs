using CivicLens.Infrastructure.Review;

namespace CivicLens.Infrastructure.Votes;

internal static class VoteReviewQueries
{
    public static readonly string UnpublishedApproved = ReviewQueries.UnpublishedApproved("vote", "vote_drafts", "record_id");

    public static readonly string CodeApprovedUnpublished = ReviewQueries.CodeApprovedOnly(UnpublishedApproved, "vote_review_decisions");
}
