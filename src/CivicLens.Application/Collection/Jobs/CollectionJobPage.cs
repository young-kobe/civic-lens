namespace CivicLens.Application.Collection.Jobs;

public sealed record CollectionJobPage(IReadOnlyList<CollectionJobRecord> Items, string? NewerCursor, string? OlderCursor);
