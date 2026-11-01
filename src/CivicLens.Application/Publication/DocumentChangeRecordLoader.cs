using System.Collections.Immutable;
using CivicLens.Application.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class DocumentChangeRecordLoader(IDocumentChangeReviewStore reviews)
    : PublishedRecordLoader<UnpublishedApprovedDraft, PublishableDocumentChange>(PublishedRecordKind.DocumentChange, "Draft")
{
    public override void ValidateId(string recordId) => ReviewValidation.ValidateDraftId(recordId);

    protected override Task<IReadOnlyList<UnpublishedApprovedDraft>> ListCandidatesAsync(int limit,
        IReadOnlyCollection<string> excludedRecordIds, CancellationToken cancellationToken) =>
        reviews.ListUnpublishedApprovedDraftsAsync(limit, excludedRecordIds, cancellationToken);

    protected override string RecordIdOf(UnpublishedApprovedDraft candidate) => candidate.Draft.DraftId;

    protected override ReadyToPublishRecord? ToReady(UnpublishedApprovedDraft candidate) =>
        Ready(candidate.Draft.CurrentStatus, candidate.Draft.UnresolvedConcernCount, candidate.Draft.DraftId,
            candidate.Draft.CurrentRevision.RevisionNumber);

    protected override Task<IReadOnlyDictionary<string, PublishableDocumentChange>> GetForPublicationAsync(
        ImmutableArray<string> recordIds, PublicationBuild build, CancellationToken cancellationToken) =>
        reviews.GetForPublicationAsync(recordIds, cancellationToken);

    protected override PendingRecord Build(string recordId, PublishableDocumentChange addition, PublicationBuild build)
    {
        var review = addition.Review;
        RequirePublishable(recordId, review.CurrentRevision.RevisionNumber, review.Decisions, review.UnresolvedConcerns, build);
        var record = new PublishedRecordBuilder(build.Rules).Build(addition, build.Previous.FirstPublishedAt(Kind, recordId), build.Corrections.For(Kind, recordId, review.CurrentRevision.RevisionNumber, review.Decisions), build.Now);
        return PendingRecord.Create(record.ToEntry(), review.ReviewStateVersion, record,
            (_, cancellationToken) => build.Renderer.RenderRecordAsync(record, build.Catalog, cancellationToken));
    }

    public override Task<string> RenderCarriedAsync(string recordId, byte[] json, ReleaseLinks links, PublicationBuild build,
        CancellationToken cancellationToken) =>
        build.Renderer.RenderRecordAsync(Parse<PublishedDocumentChange>(json), build.Catalog, cancellationToken);
}
