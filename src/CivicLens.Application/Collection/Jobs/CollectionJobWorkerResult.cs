namespace CivicLens.Application.Collection.Jobs;

public sealed record CollectionJobWorkerResult(int Passes, int JobsVisited, int JobFailures, int QueueFailures);
