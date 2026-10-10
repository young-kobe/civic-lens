using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Publication;

public sealed class ListPublishableDocumentChanges(IDocumentChangeReviewStore reviews)
{
    public async Task<ReadyToPublishList> ExecuteAsync(ReviewActor actor, CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireOwner(actor);
        var limit = PublishDocumentChanges.MaximumDrafts;
        var candidates = await reviews.ListUnpublishedApprovedDraftsAsync(limit + 1, cancellationToken);
        var ready = candidates
            .Where(candidate => PublishedRecordBuilder.IsPublishable(candidate.Draft.CurrentStatus, candidate.Draft.UnresolvedConcernCount))
            .Take(limit)
            .Select(candidate => new ReadyToPublishDraft(candidate.Draft.DraftId, candidate.Draft.CurrentRevision.Headline,
                candidate.Draft.CurrentRevision.RevisionNumber, candidate.PublishedRevisionNumber));
        return new([.. ready], candidates.Count > limit);
    }
}
