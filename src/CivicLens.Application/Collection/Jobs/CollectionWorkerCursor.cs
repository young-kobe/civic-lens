namespace CivicLens.Application.Collection.Jobs;

/// <summary>Keyset position for a fair scan over durable jobs.</summary>
public sealed record CollectionWorkerCursor(long CreatedAtUtcTicks, string JobId);
