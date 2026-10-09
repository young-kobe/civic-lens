namespace CivicLens.Application.Collection.Jobs;

public sealed record CollectionJobEvent(string JobId, string SourceId, DateTimeOffset OccurredAt);

public sealed record CollectionJobActivity(IReadOnlyList<CollectionJobEvent> Started,
    IReadOnlyList<CollectionJobEvent> Failed);
