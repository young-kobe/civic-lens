using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Infrastructure.Review;

internal static class ReviewWriteRules
{
    public static void RequireRevisionAuthor(string authorSubject, string actorSubject)
    {
        if (authorSubject != actorSubject || ReviewAuthor.IsReserved(actorSubject))
            throw new UnauthorizedAccessException("Revision author does not match the authenticated subject.");
    }

    public static void RequireDecisionActor(ReviewDecision decision, string actorSubject)
    {
        ArgumentNullException.ThrowIfNull(decision);
        if (decision.ActorSubject != actorSubject || ReviewAuthor.IsReserved(actorSubject))
            throw new UnauthorizedAccessException("Decision actor does not match the authenticated subject.");
    }

    public static void RequireSaveable(int currentRevisionNumber, int expectedRevisionNumber, int newRevisionNumber)
    {
        if (currentRevisionNumber != expectedRevisionNumber || newRevisionNumber != expectedRevisionNumber + 1)
            throw new ReviewConflictException("The draft has a newer revision. Refresh before saving.");
    }

    public static void RequireDecidable(int currentRevisionNumber, int reviewStateVersion, int expectedRevisionNumber,
        int expectedReviewStateVersion, ReviewDecision decision)
    {
        if (currentRevisionNumber != expectedRevisionNumber || decision.RevisionNumber != expectedRevisionNumber)
            throw new ReviewConflictException("The draft has a newer revision. Refresh before deciding.");
        if (reviewStateVersion != expectedReviewStateVersion || decision.ReviewStateVersion != expectedReviewStateVersion + 1)
            throw new ReviewConflictException("Review state changed. Refresh before deciding.");
    }
}
