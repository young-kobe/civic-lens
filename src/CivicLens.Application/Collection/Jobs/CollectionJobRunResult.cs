namespace CivicLens.Application.Collection.Jobs;

public enum CollectionJobRunStatus
{
    NotClaimed, Blocked, Exhausted, Cancelled, Reconciled, RecoveryBlocked,
    Completed, ImportPending, AttemptFailed, LostOwnership
}

public sealed record CollectionJobRunResult(CollectionJobRunStatus Status, CollectionJobRecord? Job,
    CollectionJobBlockReason? BlockReason = null, DateTimeOffset? RetryAt = null);
