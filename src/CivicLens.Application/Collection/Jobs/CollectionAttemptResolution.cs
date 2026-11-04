namespace CivicLens.Application.Collection.Jobs;

public enum CollectionJobAttemptOutcome { Succeeded, RetryableFailure, PermanentFailure, Interrupted }

public sealed record CollectionAttemptResolution(CollectionJobAttemptOutcome Outcome, string? ErrorCode = null,
    TimeSpan? RetryDelay = null, long? RobotsCrawlDelayMilliseconds = null);
