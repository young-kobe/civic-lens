using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class DocumentChangeRecordLoader(IDocumentChangeReviewStore reviews)
    : PublishedRecordLoader<UnpublishedApprovedDraft, PublishableDocumentChange>(PublishedRecordKind.DocumentChange, "Draft")
{
    public override void ValidateId(string recordId) => ReviewValidation.ValidateDraftId(recordId);

    protected override Task<IReadOnlyList<UnpublishedApprovedDraft>> ListCandidatesAsync(int limit,
        CancellationToken cancellationToken) => reviews.ListUnpublishedApprovedDraftsAsync(limit, cancellationToken);

    protected override ReadyToPublishRecord? ToReady(UnpublishedApprovedDraft candidate) =>
        Ready(candidate.Draft.CurrentStatus, candidate.Draft.UnresolvedConcernCount, candidate.Draft.DraftId,
            candidate.Draft.CurrentRevision.Headline, candidate.Draft.CurrentRevision.RevisionNumber, candidate.PublishedRevisionNumber);

    protected override Task<IReadOnlyDictionary<string, PublishableDocumentChange>> GetForPublicationAsync(
        ImmutableArray<string> recordIds, PublicationBuild build, CancellationToken cancellationToken) =>
        reviews.GetForPublicationAsync(recordIds, cancellationToken);

    protected override PendingRecord Build(string recordId, PublishableDocumentChange addition, PublicationBuild build)
    {
        var review = addition.Review;
        RequirePublishable(recordId, review.CurrentRevision.RevisionNumber, review.Decisions, review.UnresolvedConcerns, build.Previous);
        var record = new PublishedRecordBuilder(build.Rules).Build(addition, build.Previous.FirstPublishedAt(Kind, recordId), build.Corrections.For(Kind, recordId), build.Now);
        return PendingRecord.Create(record.ToEntry(), review.ReviewStateVersion, record,
            (_, cancellationToken) => build.Renderer.RenderRecordAsync(record, build.Catalog, cancellationToken));
    }
}
