using System.Collections.Immutable;
using CivicLens.Application.Actions;
using CivicLens.Application.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class ActionRecordLoader(IActionReviewStore actions)
    : PublishedRecordLoader<UnpublishedApprovedAction, PublishableAction>(PublishedRecordKind.Action, "Action")
{
    public override void ValidateId(string recordId) => ReviewValidation.ValidateHash(recordId, nameof(recordId));

    protected override Task<IReadOnlyList<UnpublishedApprovedAction>> ListCandidatesAsync(int limit,
        IReadOnlyCollection<string> excludedRecordIds, CancellationToken cancellationToken) =>
        actions.ListUnpublishedApprovedAsync(limit, excludedRecordIds, cancellationToken);

    protected override string RecordIdOf(UnpublishedApprovedAction candidate) => candidate.Action.RecordId;

    protected override ReadyToPublishRecord? ToReady(UnpublishedApprovedAction candidate) =>
        Ready(candidate.Action.CurrentStatus, candidate.Action.UnresolvedConcerns.Length, candidate.Action.RecordId,
            candidate.Action.CurrentRevision.RevisionNumber);

    protected override Task<IReadOnlyDictionary<string, PublishableAction>> GetForPublicationAsync(
        ImmutableArray<string> recordIds, PublicationBuild build, CancellationToken cancellationToken) =>
        actions.GetForPublicationAsync(recordIds, cancellationToken);

    protected override PendingRecord Build(string recordId, PublishableAction addition, PublicationBuild build)
    {
        var review = addition.Review;
        RequirePublishable(recordId, review.CurrentRevision.RevisionNumber, review.Decisions, review.UnresolvedConcerns, build);
        var built = new PublishedActionBuilder(build.Rules).Build(addition, build.FirstPublishedAt(Kind, recordId), [], build.Now);
        // Only code text names a rewrite of a bill action. PublishedCorrection.ValidateAll holds the rule.
        var textRewrite = built.TextOrigin == PublishedTextOrigin.Code && build.Corrections.EarlierAction(recordId) is { } earlier &&
            built.IsTextRewriteOf(earlier)
            ? PublishedTextRewrite.Code
            : PublishedTextRewrite.None;
        var record = built with
        {
            Corrections = build.Corrections.For(Kind, recordId, review.CurrentRevision.RevisionNumber, review.Decisions, textRewrite)
        };
        PublishedCorrection.ValidateAll(record.Corrections, record.RevisionNumber, Kind);
        return PendingRecord.Create(BillLine.EntryOf(record), review.ReviewStateVersion, record, record.Corrections);
    }
}
