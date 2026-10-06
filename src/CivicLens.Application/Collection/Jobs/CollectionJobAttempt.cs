using CivicLens.Collection.Contracts;

namespace CivicLens.Application.Collection.Jobs;

public sealed record CollectionJobAttempt(string AttemptId, CollectionRequest Request,
    CollectionAttemptResolution? Resolution, DateTimeOffset StartedAt, DateTimeOffset? CompletedAt);
