using CivicLens.Application.Review;
using CivicLens.Core.Review;
using CivicLens.Core.Votes;

namespace CivicLens.Application.Votes;

public sealed class ListVoteTriage(IVoteReviewStore store)
{
    public async Task<ReviewTriagePage<VoteReview>> ExecuteAsync(ReviewActor actor, string? cursor, DraftListFilter filter = default,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        ReviewValidation.ValidateListFilter(filter);
        var read = await store.ListTriageAsync(ReviewValidation.ValidatePage(cursor, ReviewTriage.PageSize), filter, cancellationToken);
        return new([.. read.Items.Select(review => new ReviewTriageRow<VoteReview>(review,
            ApprovalCheck.Of(() => review.CurrentRevision.ValidateForApproval(read.Extractions))))],
            read.Extractions, read.NewerCursor, read.OlderCursor);
    }
}
