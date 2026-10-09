using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Activity;

public enum ActivityKind { DraftCreated, RevisionSaved, DecisionRecorded, CheckStarted, CheckFailed, ChangeFound }

/// <summary>
/// Review kinds set ActorSubject, DraftId and Headline (plus DecisionKind for DecisionRecorded).
/// Collection kinds set SourceId and JobId (plus ComparisonId for ChangeFound). Unused fields are null.
/// </summary>
public sealed record ActivityEvent(ActivityKind Kind, DateTimeOffset OccurredAt, string? ActorSubject,
    string? DraftId, string? Headline, ReviewDecisionKind? DecisionKind, string? SourceId, string? JobId,
    string? ComparisonId)
{
    internal static ActivityEvent From(ReviewActivityEvent item) => new(item.Kind switch
    {
        ReviewActivityKind.DraftCreated => ActivityKind.DraftCreated,
        ReviewActivityKind.RevisionSaved => ActivityKind.RevisionSaved,
        _ => ActivityKind.DecisionRecorded
    }, item.OccurredAt, item.ActorSubject, item.DraftId, item.Headline, item.DecisionKind, null, null, null);

    internal static ActivityEvent From(ActivityKind kind, CollectionJobEvent item) =>
        new(kind, item.OccurredAt, null, null, null, null, item.SourceId, item.JobId, null);

    internal static ActivityEvent From(CollectionChangeEvent item) => new(ActivityKind.ChangeFound, item.OccurredAt,
        null, null, null, null, item.SourceId, item.JobId, item.ComparisonId);
}
