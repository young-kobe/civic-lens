using System.Collections.Immutable;
using CivicLens.Application.Actions;
using CivicLens.Application.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class ActionRecordLoader(IActionReviewStore actions)
    : PublishedRecordLoader<UnpublishedApprovedAction, PublishableAction>(PublishedRecordKind.Action, "Action"),
      ICarriedPageLoader
{
    public override void ValidateId(string recordId) => ReviewValidation.ValidateHash(recordId, nameof(recordId));

    protected override Task<IReadOnlyList<UnpublishedApprovedAction>> ListCandidatesAsync(int limit,
        IReadOnlyCollection<string> excludedRecordIds, CancellationToken cancellationToken) =>
        actions.ListUnpublishedApprovedAsync(limit, excludedRecordIds, cancellationToken);

    protected override ReadyToPublishRecord? ToReady(UnpublishedApprovedAction candidate) =>
        Ready(candidate.Action.CurrentStatus, candidate.Action.UnresolvedConcerns.Length, candidate.Action.RecordId,
            candidate.Action.CurrentRevision.Headline, candidate.Action.CurrentRevision.RevisionNumber, candidate.PublishedRevisionNumber);

    protected override Task<IReadOnlyDictionary<string, PublishableAction>> GetForPublicationAsync(
        ImmutableArray<string> recordIds, PublicationBuild build, CancellationToken cancellationToken) =>
        actions.GetForPublicationAsync(recordIds, cancellationToken);

    protected override PendingRecord Build(string recordId, PublishableAction addition, PublicationBuild build)
    {
        var review = addition.Review;
        RequirePublishable(recordId, review.CurrentRevision.RevisionNumber, review.Decisions, review.UnresolvedConcerns, build.Previous);
        var record = new PublishedActionBuilder(build.Rules).Build(addition, build.Previous.FirstPublishedAt(Kind, recordId), build.Corrections.For(Kind, recordId, review.CurrentRevision.RevisionNumber, review.Decisions), build.Now);
        return PendingRecord.Create(record.ToEntry(), review.ReviewStateVersion, record,
            (related, cancellationToken) => build.Renderer.RenderActionAsync(record, related, build.Catalog, cancellationToken));
    }

    public Task<string> RenderCarriedAsync(string recordId, byte[] json, ReleaseLinks links, PublicationBuild build,
        CancellationToken cancellationToken) =>
        build.Renderer.RenderActionAsync(Parse<PublishedAction>(json), links.Citing(Kind, recordId), build.Catalog, cancellationToken);
}
