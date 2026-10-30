using System.Collections.Immutable;
using CivicLens.Application.ExecutiveActions;
using CivicLens.Application.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class ExecutiveActionRecordLoader(IExecutiveActionReviewStore actions)
    : PublishedRecordLoader<UnpublishedApprovedExecutiveAction, PublishableExecutiveAction>(PublishedRecordKind.ExecutiveAction, "Executive action")
{
    public override void ValidateId(string recordId) => ReviewValidation.ValidateHash(recordId, nameof(recordId));

    protected override Task<IReadOnlyList<UnpublishedApprovedExecutiveAction>> ListCandidatesAsync(int limit,
        IReadOnlyCollection<string> excludedRecordIds, CancellationToken cancellationToken) =>
        actions.ListUnpublishedApprovedAsync(limit, excludedRecordIds, cancellationToken);

    protected override string RecordIdOf(UnpublishedApprovedExecutiveAction candidate) => candidate.Action.RecordId;

    protected override ReadyToPublishRecord? ToReady(UnpublishedApprovedExecutiveAction candidate) =>
        Ready(candidate.Action.CurrentStatus, candidate.Action.UnresolvedConcerns.Length, candidate.Action.RecordId,
            candidate.Action.CurrentRevision.Title, candidate.Action.CurrentRevision.RevisionNumber, candidate.PublishedRevisionNumber);

    protected override Task<IReadOnlyDictionary<string, PublishableExecutiveAction>> GetForPublicationAsync(
        ImmutableArray<string> recordIds, PublicationBuild build, CancellationToken cancellationToken) =>
        actions.GetForPublicationAsync(recordIds, cancellationToken);

    protected override PendingRecord Build(string recordId, PublishableExecutiveAction addition, PublicationBuild build)
    {
        var review = addition.Review;
        RequirePublishable(recordId, review.CurrentRevision.RevisionNumber, review.Decisions, review.UnresolvedConcerns, build.Previous);
        var record = new PublishedExecutiveActionBuilder(build.Rules).Build(addition, build.Previous.FirstPublishedAt(Kind, recordId),
            build.Corrections.For(Kind, recordId, review.CurrentRevision.RevisionNumber, review.Decisions), build.Now);
        return PendingRecord.Create(record.ToEntry(), review.ReviewStateVersion, record,
            (_, cancellationToken) => build.Renderer.RenderExecutiveActionAsync(record, cancellationToken));
    }
}
