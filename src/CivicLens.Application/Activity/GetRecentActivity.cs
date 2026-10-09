using CivicLens.Application.Collection.Jobs;
using CivicLens.Application.Collection.Processing;
using CivicLens.Application.Review;
using CivicLens.Core.Review;

namespace CivicLens.Application.Activity;

/// <summary>Newest review events for any reviewer; the owner also sees collection events when collection is configured.</summary>
public sealed class GetRecentActivity(IDocumentChangeReviewStore reviews, ICollectionJobStore? jobs = null,
    IEvidenceProcessingStore? processing = null)
{
    public const int DefaultLimit = 8;
    public const int MaximumLimit = 50;

    public async Task<IReadOnlyList<ActivityEvent>> ExecuteAsync(ReviewActor actor, int limit = DefaultLimit,
        CancellationToken cancellationToken = default)
    {
        ReviewAuthorization.RequireReviewer(actor);
        if (limit is < 1 or > MaximumLimit) throw new ArgumentOutOfRangeException(nameof(limit));
        var events = (await reviews.ListRecentActivityAsync(limit, cancellationToken)).Select(ActivityEvent.From).ToList();
        if (actor.Role == ReviewRole.Owner) events.AddRange(await ReadCollectionEventsAsync(limit, cancellationToken));
        return events.OrderByDescending(item => item.OccurredAt).ThenBy(item => item.Kind)
            .ThenBy(item => item.DraftId ?? item.JobId, StringComparer.Ordinal).Take(limit).ToArray();
    }

    private async Task<IEnumerable<ActivityEvent>> ReadCollectionEventsAsync(int limit, CancellationToken cancellationToken)
    {
        var events = Enumerable.Empty<ActivityEvent>();
        if (jobs is not null)
        {
            var activity = await jobs.ListActivityAsync(limit, cancellationToken);
            events = activity.Started.Select(item => ActivityEvent.From(ActivityKind.CheckStarted, item))
                .Concat(activity.Failed.Select(item => ActivityEvent.From(ActivityKind.CheckFailed, item)));
        }
        if (processing is null) return events;
        var changes = await processing.ListRecentChangesAsync(limit, cancellationToken);
        return events.Concat(changes.Select(ActivityEvent.From));
    }
}
