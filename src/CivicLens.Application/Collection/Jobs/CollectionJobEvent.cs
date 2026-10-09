namespace CivicLens.Application.Collection.Jobs;

public sealed record CollectionJobEvent(string JobId, string SourceId, DateTimeOffset OccurredAt);

/// <summary>The newest jobs by creation time, and the newest failed jobs by failure time.</summary>
public sealed record CollectionJobActivity(IReadOnlyList<CollectionJobEvent> Started,
    IReadOnlyList<CollectionJobEvent> Failed);
