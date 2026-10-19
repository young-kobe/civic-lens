using CivicLens.Application.Review;
using CivicLens.Core.Actions;
using CivicLens.Core.Review;

namespace CivicLens.Application.Actions;

public sealed class ListActionTriage(IActionReviewStore store)
{
    public async Task<ReviewTriagePage<ActionReview>> ExecuteAsync(ReviewActor actor, string? cursor, DraftListFilter filter = default,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        ReviewValidation.ValidateListFilter(filter);
        var read = await store.ListTriageAsync(ReviewValidation.ValidatePage(cursor, ReviewTriage.PageSize), filter, cancellationToken);
        return new([.. read.Items.Select(review => new ReviewTriageRow<ActionReview>(review,
            ApprovalCheck.Of(() => review.CurrentRevision.ValidateForApproval(read.Extractions)), read.Priorities[review.RecordId]))],
            read.Extractions, read.NewerCursor, read.OlderCursor);
    }
}
