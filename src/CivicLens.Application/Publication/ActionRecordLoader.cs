using System.Collections.Immutable;
using CivicLens.Application.Actions;
using CivicLens.Application.Review;
using CivicLens.Publication.Contracts;

namespace CivicLens.Application.Publication;

internal sealed class ActionRecordLoader(IActionReviewStore actions) : PublishedRecordLoader(PublishedRecordKind.Action, "Action")
{
    public override void ValidateId(string recordId) => ReviewValidation.ValidateHash(recordId, nameof(recordId));

    public override async Task<(IReadOnlyList<ReadyToPublishRecord> Ready, bool HasMore)> ListReadyAsync(int limit,
        CancellationToken cancellationToken)
    {
        var candidates = await actions.ListUnpublishedApprovedAsync(limit + 1, cancellationToken);
        var ready = candidates
            .Where(candidate => PublishedRecordRules.IsPublishable(candidate.Action.CurrentStatus, candidate.Action.UnresolvedConcerns.Length))
            .Select(candidate => Ready(candidate.Action.RecordId, candidate.Action.CurrentRevision.Headline,
                candidate.Action.CurrentRevision.RevisionNumber, candidate.PublishedRevisionNumber))
            .ToList();
        return (ready, candidates.Count > limit);
    }

    public override async Task<List<PendingRecord>> LoadAsync(ImmutableArray<string> recordIds, PublicationBuild build,
        CancellationToken cancellationToken)
    {
        if (recordIds.IsEmpty) return [];
        var found = await actions.GetForPublicationAsync(recordIds, cancellationToken);
        var builder = new PublishedActionBuilder(build.Rules);
        var pending = new List<PendingRecord>(recordIds.Length);
        foreach (var recordId in recordIds)
        {
            var addition = found.GetValueOrDefault(recordId) ?? throw NotFound(recordId);
            var review = addition.Review;
            RequirePublishable(recordId, review.CurrentRevision.RevisionNumber, review.Decisions, review.UnresolvedConcerns, build.Previous);
            var record = builder.Build(addition, build.Previous.Find(Kind, recordId), build.Now);
            pending.Add(PendingRecord.Create(record.ToEntry(), review.ReviewStateVersion, record,
                await build.Renderer.RenderActionAsync(record, cancellationToken)));
        }
        return pending;
    }
}
