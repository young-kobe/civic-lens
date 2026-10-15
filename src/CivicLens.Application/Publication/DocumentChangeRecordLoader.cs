using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class DocumentChangeRecordLoader(IDocumentChangeReviewStore reviews)
    : PublishedRecordLoader(PublishedRecordKind.DocumentChange, "Draft")
{
    public override void ValidateId(string recordId) => ReviewValidation.ValidateDraftId(recordId);

    public override async Task<(IReadOnlyList<ReadyToPublishRecord> Ready, bool HasMore)> ListReadyAsync(int limit,
        CancellationToken cancellationToken)
    {
        var candidates = await reviews.ListUnpublishedApprovedDraftsAsync(limit + 1, cancellationToken);
        var ready = candidates
            .Where(candidate => PublishedRecordRules.IsPublishable(candidate.Draft.CurrentStatus, candidate.Draft.UnresolvedConcernCount))
            .Select(candidate => Ready(candidate.Draft.DraftId, candidate.Draft.CurrentRevision.Headline,
                candidate.Draft.CurrentRevision.RevisionNumber, candidate.PublishedRevisionNumber))
            .ToList();
        return (ready, candidates.Count > limit);
    }

    public override async Task<List<PendingRecord>> LoadAsync(ImmutableArray<string> recordIds, PublicationBuild build,
        CancellationToken cancellationToken)
    {
        if (recordIds.IsEmpty) return [];
        var found = await reviews.GetForPublicationAsync(recordIds, cancellationToken);
        var builder = new PublishedRecordBuilder(build.Rules);
        var pending = new List<PendingRecord>(recordIds.Length);
        foreach (var draftId in recordIds)
        {
            var addition = found.GetValueOrDefault(draftId) ?? throw NotFound(draftId);
            var review = addition.Review;
            RequirePublishable(draftId, review.CurrentRevision.RevisionNumber, review.Decisions, review.UnresolvedConcerns, build.Previous);
            var record = builder.Build(addition, build.Previous.Find(Kind, draftId), build.Now);
            pending.Add(PendingRecord.Create(record.ToEntry(), review.ReviewStateVersion, record,
                await build.Renderer.RenderRecordAsync(record, cancellationToken)));
        }
        return pending;
    }
}
