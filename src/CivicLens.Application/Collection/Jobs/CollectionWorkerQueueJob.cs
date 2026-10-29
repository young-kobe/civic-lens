namespace CivicLens.Application.Collection.Jobs;

/// <summary>An eligible job and the pacing key that decides which jobs may not run at the same time.</summary>
public sealed record CollectionWorkerQueueJob(string JobId, string PacingKey);
