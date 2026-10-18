using System.Collections.Immutable;
using CivicLens.Application.Briefs;
using CivicLens.Application.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class BriefRecordLoader(IBriefReviewStore briefs)
    : PublishedRecordLoader<UnpublishedApprovedBrief, PublishableBrief>(PublishedRecordKind.Brief, "Brief")
{
    public override void ValidateId(string recordId) => ReviewValidation.ValidateHash(recordId, nameof(recordId));

    protected override Task<IReadOnlyList<UnpublishedApprovedBrief>> ListCandidatesAsync(int limit,
        CancellationToken cancellationToken) => briefs.ListUnpublishedApprovedAsync(limit, cancellationToken);

    protected override ReadyToPublishRecord? ToReady(UnpublishedApprovedBrief candidate) =>
        candidate.CitedRecordsChanged
            ? null
            : Ready(candidate.Brief.CurrentStatus, candidate.Brief.UnresolvedConcerns.Length, candidate.Brief.RecordId,
                candidate.Brief.CurrentRevision.Headline, candidate.Brief.CurrentRevision.RevisionNumber, candidate.PublishedRevisionNumber);

    protected override Task<IReadOnlyDictionary<string, PublishableBrief>> GetForPublicationAsync(
        ImmutableArray<string> recordIds, PublicationBuild build, CancellationToken cancellationToken) =>
        briefs.GetForPublicationAsync(recordIds, build.ReleaseRecords, cancellationToken);

    protected override PendingRecord Build(string recordId, PublishableBrief addition, PublicationBuild build)
    {
        var review = addition.Review;
        RequirePublishable(recordId, review.CurrentRevision.RevisionNumber, review.Decisions, review.UnresolvedConcerns, build.Previous);
        if (addition.CitedRecordsChanged)
            throw new ArgumentException(
                $"Brief {recordId} cites a record that the new release does not hold at the cited revision, or that is not approved.");
        var record = PublishedBriefBuilder.Build(addition, build.Previous.Find(Kind, recordId), build.Now);
        return PendingRecord.Create(record.ToEntry(), review.ReviewStateVersion, record,
            (_, cancellationToken) => build.Renderer.RenderBriefAsync(record, cancellationToken));
    }
}
