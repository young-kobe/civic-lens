namespace CivicLens.Application.Collection.Jobs;

/// <summary>Keyset position in the claim order of durable jobs: priority, creation time, then job ID.</summary>
public sealed record CollectionWorkerCursor(int Priority, long CreatedAtUtcTicks, string JobId);
